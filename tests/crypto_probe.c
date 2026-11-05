/* Diagnostic only: same executable, candidate OpenSSL/libc, no Tor state. */
#include <openssl/evp.h>
#include <openssl/crypto.h>
#include <stdio.h>
#include <stdlib.h>
#include <time.h>
#include <sys/resource.h>

static double now(clockid_t id) {
    struct timespec ts;
    if (clock_gettime(id, &ts)) exit(2);
    return ts.tv_sec + ts.tv_nsec / 1e9;
}
static void require(int ok) { if (!ok) exit(3); }
static EVP_PKEY *key(int type) {
    EVP_PKEY_CTX *c=EVP_PKEY_CTX_new_id(type,NULL); EVP_PKEY *p=NULL;
    require(c && EVP_PKEY_keygen_init(c)>0 && EVP_PKEY_keygen(c,&p)>0);
    EVP_PKEY_CTX_free(c); return p;
}
int main(void) {
    unsigned char in[16384]={0},out[16416],k[16]={0},iv[16]={0},digest[64];
    double wall,cpu; int n; unsigned int dn;
    printf("{\"openssl\":\"%s\",\"trials\":[",OpenSSL_version(OPENSSL_VERSION));
    for (int trial=0; trial<3;trial++) {
        if(trial) printf(",");
        EVP_CIPHER_CTX *cipher=EVP_CIPHER_CTX_new(); require(cipher!=NULL);
        require(EVP_EncryptInit_ex(cipher,EVP_aes_128_ctr(),NULL,k,iv)>0);
        wall=now(CLOCK_MONOTONIC);cpu=now(CLOCK_PROCESS_CPUTIME_ID);
        for(int i=0;i<2048;i++) require(EVP_EncryptUpdate(cipher,out,&n,in,sizeof(in))>0);
        printf("{\"aes128ctr_bytes_per_second\":%.3f,\"aes_cpu_seconds\":%.6f,",33554432.0/(now(CLOCK_MONOTONIC)-wall),now(CLOCK_PROCESS_CPUTIME_ID)-cpu);
        EVP_CIPHER_CTX_free(cipher);
        EVP_MD_CTX *md=EVP_MD_CTX_new();require(md!=NULL);
        wall=now(CLOCK_MONOTONIC);cpu=now(CLOCK_PROCESS_CPUTIME_ID);
        for(int i=0;i<2048;i++) require(EVP_DigestInit_ex(md,EVP_sha256(),NULL)>0 && EVP_DigestUpdate(md,in,sizeof(in))>0 && EVP_DigestFinal_ex(md,digest,&dn)>0);
        printf("\"sha256_bytes_per_second\":%.3f,\"sha_cpu_seconds\":%.6f,",33554432.0/(now(CLOCK_MONOTONIC)-wall),now(CLOCK_PROCESS_CPUTIME_ID)-cpu);
        EVP_MD_CTX_free(md);
        EVP_PKEY *a=key(EVP_PKEY_X25519),*b=key(EVP_PKEY_X25519);
        EVP_PKEY_CTX *derive=EVP_PKEY_CTX_new(a,NULL);
        require(derive && EVP_PKEY_derive_init(derive)>0 && EVP_PKEY_derive_set_peer(derive,b)>0);
        wall=now(CLOCK_MONOTONIC);cpu=now(CLOCK_PROCESS_CPUTIME_ID);
        for(int i=0;i<500;i++){size_t len=sizeof(out);require(EVP_PKEY_derive(derive,out,&len)>0);}
        printf("\"x25519_ops_per_second\":%.3f,\"x25519_cpu_seconds\":%.6f}",500.0/(now(CLOCK_MONOTONIC)-wall),now(CLOCK_PROCESS_CPUTIME_ID)-cpu);
        EVP_PKEY_CTX_free(derive);EVP_PKEY_free(a);EVP_PKEY_free(b);
    }
    struct rusage usage;require(getrusage(RUSAGE_SELF,&usage)==0);
    printf("],\"maxrss_kib\":%ld}\n",usage.ru_maxrss);
    return 0;
}
