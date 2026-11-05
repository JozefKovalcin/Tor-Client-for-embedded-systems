/* Test-only native observer/proxy. Never installed in a production image. */
#define _GNU_SOURCE
#include <arpa/inet.h>
#include <errno.h>
#include <fcntl.h>
#include <poll.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/socket.h>
#include <sys/statvfs.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <time.h>
#include <unistd.h>
#include <openssl/crypto.h>
#include <openssl/evp.h>
#include <openssl/hmac.h>
#include <openssl/rand.h>

static pid_t observer,backend;
static const char *pidfile="/run/tor/tor.pid";
static int port=19090;
static double monotonic(void){struct timespec t;clock_gettime(CLOCK_MONOTONIC,&t);return t.tv_sec+t.tv_nsec/1e9;}
static int transfer(int fd,void *buf,size_t n,int writing){char *p=buf;while(n){ssize_t k=writing?send(fd,p,n,MSG_NOSIGNAL):recv(fd,p,n,0);if(k<=0)return -1;p+=k;n-=k;}return 0;}
static int listener(const char *ip,int number){int s=socket(AF_INET,SOCK_STREAM,0),yes=1;struct sockaddr_in a={.sin_family=AF_INET,.sin_port=htons(number)};if(s<0)return -1;setsockopt(s,SOL_SOCKET,SO_REUSEADDR,&yes,sizeof(yes));inet_pton(AF_INET,ip,&a.sin_addr);if(bind(s,(void*)&a,sizeof(a))||listen(s,32)){close(s);return -1;}return s;}
static int connect_local(int number){int s=socket(AF_INET,SOCK_STREAM,0);struct sockaddr_in a={.sin_family=AF_INET,.sin_port=htons(number),.sin_addr.s_addr=htonl(INADDR_LOOPBACK)};if(s<0)return -1;if(connect(s,(void*)&a,sizeof(a))){close(s);return -1;}return s;}
static void relay(int a,int b){char buf[16384];struct pollfd p[2]={{a,POLLIN,0},{b,POLLIN,0}};while(poll(p,2,-1)>0){for(int i=0;i<2;i++)if(p[i].revents&(POLLIN|POLLHUP|POLLERR)){ssize_t n=recv(p[i].fd,buf,sizeof(buf),0);if(n<=0||transfer(p[1-i].fd,buf,n,1))return;}}}
static int line(int fd,char *s,size_t max){size_t n=0;while(n+1<max){char c;if(recv(fd,&c,1,0)!=1)return -1;if(c=='\n'){s[n]=0;return (int)n;}if(c!='\r')s[n++]=c;}return -1;}
static long long value(const char *file,const char *key,int kib){FILE *f=fopen(file,"r");if(!f)return -1;char *s=NULL;size_t n=0;long long answer=-1;size_t k=strlen(key);while(getline(&s,&n,f)>0){if(!strncmp(s,key,k)&&(s[k]==':'||s[k]==' ')){char *p=s+k;while(*p==':'||*p==' ')p++;answer=strtoll(p,NULL,10)*(kib?1024:1);break;}}free(s);fclose(f);return answer;}
static long long procvalue(pid_t pid,const char *suffix,const char *key){char path[128];snprintf(path,sizeof(path),"/proc/%ld/%s",(long)pid,suffix);return value(path,key,1);}
static void number(int fd,const char *name,long long x,int comma){dprintf(fd,"%s\"%s\":",comma?",":"",name);if(x<0)dprintf(fd,"null");else dprintf(fd,"%lld",x);}
static pid_t torpid(void){FILE *f=fopen(pidfile,"r");long p=-1;if(f){if(fscanf(f,"%ld",&p)!=1)p=-1;fclose(f);}return (pid_t)p;}
static void process_stats(int fd,pid_t pid){char path[128],buf[4096];snprintf(path,sizeof(path),"/proc/%ld/stat",(long)pid);FILE *f=fopen(path,"r");if(!f||!fgets(buf,sizeof(buf),f)){if(f)fclose(f);dprintf(fd,"null");return;}fclose(f);char *s=strrchr(buf,')');if(!s){dprintf(fd,"null");return;}s+=2;if(*s=='Z'){dprintf(fd,"null");return;}long long fields[64]={0};int i=0;char *save=NULL;for(char *t=strtok_r(s," ",&save);t&&i<64;t=strtok_r(NULL," ",&save))fields[i++]=strtoll(t,NULL,10);dprintf(fd,"{\"pid\":%ld,\"cpu_ticks\":%lld,\"minor_faults\":%lld,\"major_faults\":%lld",(long)pid,fields[11]+fields[12],fields[7],fields[9]);number(fd,"rss_bytes",procvalue(pid,"status","VmRSS"),1);number(fd,"hwm_bytes",procvalue(pid,"status","VmHWM"),1);number(fd,"pss_bytes",procvalue(pid,"smaps_rollup","Pss"),1);snprintf(path,sizeof(path),"/proc/%ld/io",(long)pid);number(fd,"read_bytes",value(path,"read_bytes",0),1);number(fd,"write_bytes",value(path,"write_bytes",0),1);dprintf(fd,"}");}
static void snapshot(int fd){
    static const char *mem[]={"MemTotal","MemAvailable","MemFree","Cached","Shmem","Slab","Dirty","Writeback","SwapTotal","SwapFree",NULL};
    static const char *vm[]={"pgfault","pgmajfault","pgscan_kswapd","pgscan_direct","allocstall_dma32","allocstall_normal","compact_stall","pswpin","pswpout","oom_kill",NULL};
    dprintf(fd,"{\"time\":%.6f,\"hz\":%ld,\"memory\":{",monotonic(),sysconf(_SC_CLK_TCK));for(int i=0;mem[i];i++)number(fd,mem[i],value("/proc/meminfo",mem[i],1),i>0);dprintf(fd,"},\"vmstat\":{");for(int i=0;vm[i];i++)number(fd,vm[i],value("/proc/vmstat",vm[i],0),i>0);
    dprintf(fd,"},\"tor\":");process_stats(fd,torpid());dprintf(fd,",\"observer\":[");process_stats(fd,observer);dprintf(fd,",");process_stats(fd,backend);dprintf(fd,",");process_stats(fd,getpid());dprintf(fd,"]");
    struct statvfs v;if(!statvfs("/var/lib/tor",&v))dprintf(fd,",\"state_volume_bytes\":%llu,\"state_available_bytes\":%llu",(unsigned long long)v.f_blocks*v.f_frsize,(unsigned long long)v.f_bavail*v.f_frsize);
    FILE *f=fopen("/proc/stat","r");unsigned long long u,n,s,idle,wait,irq,soft,steal;if(f){if(fscanf(f,"cpu %llu %llu %llu %llu %llu %llu %llu %llu",&u,&n,&s,&idle,&wait,&irq,&soft,&steal)==8)dprintf(fd,",\"system_cpu\":{\"busy\":%llu,\"idle\":%llu,\"iowait\":%llu}",u+n+s+irq+soft+steal,idle,wait);fclose(f);}dprintf(fd,"}\n");
}
static void hex(const unsigned char *in,char *out,size_t n){for(size_t i=0;i<n;i++)sprintf(out+2*i,"%02x",in[i]);}
static int unhex(const char *in,unsigned char *out,size_t n){for(size_t i=0;i<n;i++){unsigned int v;if(sscanf(in+2*i,"%2x",&v)!=1)return -1;out[i]=(unsigned char)v;}return 0;}
/* SAFECOOKIE is completed locally: neither cookies nor private keys leave the guest. */
static int authenticated_control(void){unsigned char cookie[32],nonce[32],server_nonce[32],server_hash[32],message[96],proof[32];unsigned int size=0;FILE *f=fopen("/var/lib/tor/control_auth_cookie","rb");if(!f)return -1;size_t n=fread(cookie,1,sizeof(cookie),f);fclose(f);if(n!=32||RAND_bytes(nonce,32)!=1)return -1;int s=connect_local(9051);if(s<0)return -1;char request[160],reply[1024];strcpy(request,"AUTHCHALLENGE SAFECOOKIE ");size_t prefix=strlen(request);hex(nonce,request+prefix,32);strcpy(request+prefix+64,"\r\n");if(transfer(s,request,prefix+66,1)||line(s,reply,sizeof(reply))<0||strncmp(reply,"250 ",4))goto fail;char *h=strstr(reply,"SERVERHASH="),*sn=strstr(reply,"SERVERNONCE=");if(!h||!sn||strlen(h+11)<64||strlen(sn+12)<64||unhex(h+11,server_hash,32)||unhex(sn+12,server_nonce,32))goto fail;memcpy(message,cookie,32);memcpy(message+32,nonce,32);memcpy(message+64,server_nonce,32);const char *server_key="Tor safe cookie authentication server-to-controller hash",*client_key="Tor safe cookie authentication controller-to-server hash";if(!HMAC(EVP_sha256(),server_key,strlen(server_key),message,sizeof(message),proof,&size)||size!=32||CRYPTO_memcmp(proof,server_hash,32))goto fail;if(!HMAC(EVP_sha256(),client_key,strlen(client_key),message,sizeof(message),proof,&size)||size!=32)goto fail;strcpy(request,"AUTHENTICATE ");hex(proof,request+13,32);strcpy(request+77,"\r\n");if(transfer(s,request,79,1)||line(s,reply,sizeof(reply))<0||strncmp(reply,"250 ",4))goto fail;OPENSSL_cleanse(cookie,sizeof(cookie));OPENSSL_cleanse(message,sizeof(message));return s;fail:OPENSSL_cleanse(cookie,sizeof(cookie));OPENSSL_cleanse(message,sizeof(message));close(s);return -1;}
/* Same 64 KiB server push as tests/echo_server.py; this is fixture data, not a key. */
static void echo_connection(int s){static const unsigned char digest[32]={0x52,0xef,0x74,0x0b,0x7f,0x20,0x81,0x74,0x87,0x20,0xa6,0xcc,0x3d,0x7f,0xe7,0x93,0x7b,0x74,0x34,0xd3,0xe2,0xcb,0x88,0x18,0xa3,0x42,0x4e,0x68,0x91,0x4f,0xa2,0xba};unsigned char greeting[16384];for(size_t i=0;i<sizeof(greeting);i++)greeting[i]=digest[i%32];uint32_t n=htonl(65536);if(transfer(s,&n,4,1))return;for(int i=0;i<4;i++)if(transfer(s,greeting,sizeof(greeting),1))return;for(;;){if(transfer(s,&n,4,0))return;size_t count=ntohl(n);if(count>1024*1024)return;char *b=malloc(count?count:1);if(!b)return;if(transfer(s,b,count,0)||transfer(s,&n,4,1)||transfer(s,b,count,1)){free(b);return;}free(b);}}
static void backend_loop(void){if(setgid(905)||setuid(905))_exit(10);int s=listener("127.0.0.1",18080);if(s<0)_exit(11);for(;;){int c=accept(s,NULL,NULL);if(c<0)continue;pid_t p=fork();if(p==0){close(s);struct timeval t={.tv_sec=180};setsockopt(c,SOL_SOCKET,SO_RCVTIMEO,&t,sizeof(t));setsockopt(c,SOL_SOCKET,SO_SNDTIMEO,&t,sizeof(t));echo_connection(c);close(c);_exit(0);}close(c);}}
static void serve(int s){char command[8192];if(line(s,command,sizeof(command))<0)return;if(!strcmp(command,"MONITOR")){for(;;){snapshot(s);if(errno==EPIPE||errno==ECONNRESET)return;usleep(250000);}}
    else if(!strcmp(command,"SAMPLE"))snapshot(s);
    else if(!strcmp(command,"SOCKS")||!strcmp(command,"CONTROL")||!strcmp(command,"ECHO")){int remote=!strcmp(command,"CONTROL")?authenticated_control():connect_local(!strcmp(command,"SOCKS")?9050:18080);if(remote<0){dprintf(s,"515 agent target unavailable\r\n");return;}relay(s,remote);close(remote);}
    else if(!strncmp(command,"EXEC ",5)){signal(SIGCHLD,SIG_DFL);pid_t p=fork();if(p==0){dup2(s,STDOUT_FILENO);dup2(s,STDERR_FILENO);close(s);execl("/bin/sh","sh","-c",command+5,(char*)NULL);_exit(127);}int status;if(waitpid(p,&status,0)>0)dprintf(s,"\n__CAPACITY_EXIT=%d\n",WIFEXITED(status)?WEXITSTATUS(status):128+WTERMSIG(status));}
    else dprintf(s,"515 unknown diagnostic command\r\n");
}
int main(int argc,char **argv){const char *bindip=argc>1?argv[1]:"10.0.2.15";if(argc>2)port=atoi(argv[2]);if(argc>3)pidfile=argv[3];signal(SIGPIPE,SIG_IGN);signal(SIGCHLD,SIG_IGN);observer=getpid();backend=fork();if(backend==0)backend_loop();int s=listener(bindip,port);if(s<0){perror("agent bind");return 2;}puts("CAPACITY_AGENT_READY");fflush(stdout);for(;;){int c=accept(s,NULL,NULL);if(c<0)continue;pid_t p=fork();if(p==0){close(s);signal(SIGCHLD,SIG_DFL);serve(c);close(c);_exit(0);}close(c);}}
