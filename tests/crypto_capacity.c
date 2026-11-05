/* Supplemental local crypto timing. No production keys or crypto substitutions. */
#define _POSIX_C_SOURCE 200809L
#include <openssl/evp.h>
#include <openssl/crypto.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
static double clock_seconds(clockid_t id){struct timespec t;if(clock_gettime(id,&t))exit(2);return t.tv_sec+t.tv_nsec/1e9;}
static void require(int ok){if(!ok)exit(3);}
int main(void){unsigned char in[512]={0},out[1024],key[32]={0},iv[16]={0},digest[64];unsigned int length;int count;
    printf("{\"openssl\":\"%s\",\"trials\":[",OpenSSL_version(OPENSSL_VERSION));
    for(int trial=0;trial<5;trial++){
        if(trial)printf(",");
        printf("{\"trial\":%d",trial);
        EVP_CIPHER_CTX *c=EVP_CIPHER_CTX_new();require(c!=NULL);require(EVP_EncryptInit_ex(c,EVP_aes_128_ctr(),NULL,key,iv)>0);
        double wall=clock_seconds(CLOCK_MONOTONIC),cpu=clock_seconds(CLOCK_PROCESS_CPUTIME_ID);const int loops=32768;
        for(int i=0;i<loops;i++)require(EVP_EncryptUpdate(c,out,&count,in,509)>0);
        printf(",\"aes_cell_bytes_per_second\":%.3f,\"aes_cpu_seconds\":%.6f",509.0*loops/(clock_seconds(CLOCK_MONOTONIC)-wall),clock_seconds(CLOCK_PROCESS_CPUTIME_ID)-cpu);EVP_CIPHER_CTX_free(c);
        const EVP_MD *methods[]={EVP_sha256(),EVP_sha3_256(),EVP_shake256()};const char *names[]={"sha256_cell","sha3_256_cell","shake256_cell"};
        EVP_MD_CTX *m=EVP_MD_CTX_new();require(m!=NULL);
        for(int j=0;j<3;j++){wall=clock_seconds(CLOCK_MONOTONIC);cpu=clock_seconds(CLOCK_PROCESS_CPUTIME_ID);
            for(int i=0;i<loops;i++){require(EVP_DigestInit_ex(m,methods[j],NULL)>0&&EVP_DigestUpdate(m,in,509)>0);require(j==2?EVP_DigestFinalXOF(m,digest,32)>0:EVP_DigestFinal_ex(m,digest,&length)>0);}
            printf(",\"%s_bytes_per_second\":%.3f,\"%s_cpu_seconds\":%.6f",names[j],509.0*loops/(clock_seconds(CLOCK_MONOTONIC)-wall),names[j],clock_seconds(CLOCK_PROCESS_CPUTIME_ID)-cpu);
        }EVP_MD_CTX_free(m);printf("}");
    }printf("]}\n");return 0;
}
