/* Guest-only, disposable-snapshot I/O diagnostics; never access real keys. */
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>
#include <fcntl.h>
#include <time.h>
#include <string.h>
#include <sys/stat.h>

static double clock_s(void){struct timespec t;if(clock_gettime(CLOCK_MONOTONIC,&t))exit(2);return t.tv_sec+t.tv_nsec/1e9;}
static void check(int ok){if(!ok){perror("io-probe");exit(3);}}
static unsigned long long sectors(void){
    FILE *f=fopen("/proc/diskstats","r");char line[512],name[64];unsigned ma,mi;unsigned long long rios,merges,sectors_read;
    check(f!=NULL);
    while(fgets(line,sizeof(line),f))if(sscanf(line,"%u %u %63s %llu %llu %llu",&ma,&mi,name,&rios,&merges,&sectors_read)==6 && !strcmp(name,"vda")){fclose(f);return sectors_read;}
    fclose(f);fprintf(stderr,"primary disk counters unavailable\n");exit(4);
}
int main(void){
    const char *files[]={"/usr/bin/tor","/usr/lib/libcrypto.so.3"};char buf[65536]={0};volatile unsigned checksum=0;
    printf("{\"reads\":[");
    for(int file=0;file<2;file++)for(int pass=0;pass<2;pass++){
        if(!pass){sync();int fd=open("/proc/sys/vm/drop_caches",O_WRONLY);check(fd>=0);check(write(fd,"3\n",2)==2);close(fd);}
        unsigned long long before=sectors(),bytes=0;double start=clock_s();int fd=open(files[file],O_RDONLY);check(fd>=0);ssize_t n;
        while((n=read(fd,buf,sizeof(buf)))>0){bytes+=n;checksum+=(unsigned char)buf[0];}
        check(n==0);close(fd);double elapsed=clock_s()-start;unsigned long long after=sectors();
        if(file||pass)printf(",");
        printf("{\"file\":\"%s\",\"cache\":\"%s\",\"bytes\":%llu,\"seconds\":%.6f,\"primary_disk_read_bytes\":%llu}",files[file],pass?"warm":"cold",bytes,elapsed,(after-before)*512);
    }
    check(mkdir("/var/lib/tor",0700)==0 || access("/var/lib/tor",F_OK)==0);
    const char *name="/var/lib/tor/io-probe-exclusive.tmp";int fd=open(name,O_CREAT|O_EXCL|O_WRONLY,0600);check(fd>=0);
    double start=clock_s();for(int i=0;i<16;i++)check(write(fd,buf,sizeof(buf))==sizeof(buf));check(fsync(fd)==0);close(fd);double elapsed=clock_s()-start;
    check(unlink(name)==0);sync();
    printf("],\"persistent_write_fsync_bytes\":1048576,\"persistent_write_fsync_seconds\":%.6f,\"checksum\":%u}\n",elapsed,checksum);
    return 0;
}
