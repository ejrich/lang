#if os == OS.Linux {
    #system_library ssl "ssl"
    #system_library crypto "crypto"
}
#if os == OS.Windows {
    #system_library ssl "libssl" "lib"
    #system_library crypto "libcrypto" "lib"
}

// SSL
struct SSL_METHOD {}

SSL_METHOD* TLS_method() #extern ssl
SSL_METHOD* TLS_server_method() #extern ssl
SSL_METHOD* TLS_client_method() #extern ssl

struct SSL_CTX {}

SSL_CTX* SSL_CTX_new(SSL_METHOD* meth) #extern ssl

struct SSL {}

SSL* SSL_new(SSL_CTX *ctx) #extern ssl
int SSL_accept(SSL* ssl) #extern ssl
int SSL_stateless(SSL* ssl) #extern ssl
int SSL_connect(SSL* ssl) #extern ssl
int SSL_read(SSL* ssl, void* buf, int num) #extern ssl
int SSL_peek(SSL *ssl, void *buf, int num) #extern ssl
int SSL_write(SSL* ssl, void* buf, int num) #extern ssl


// Crypto
interface void* CRYPTO_malloc_fn(u64 num, u8* file, int line)
interface void* CRYPTO_realloc_fn(void* addr, u64 num, u8* file, int line)
interface void* CRYPTO_free_fn(void* addr, u8* file, int line)

int CRYPTO_set_mem_functions(CRYPTO_malloc_fn malloc_fn, CRYPTO_realloc_fn realloc_fn, CRYPTO_free_fn free_fn) #extern crypto
