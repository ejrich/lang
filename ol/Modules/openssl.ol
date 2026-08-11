#import networking

#if os == OS.Linux {
    #system_library ssl "ssl"
    #system_library crypto "crypto"
}
#if os == OS.Windows {
    #system_library ssl "libssl" "lib"
    #copy_to_output_directory "lib/libssl-4-x64.dll"
    #system_library crypto "libcrypto" "lib"
    #copy_to_output_directory "lib/libcrypto-4-x64.dll"
}

// SSL
struct SSL_METHOD {}

SSL_METHOD* TLS_method() #extern ssl
SSL_METHOD* TLS_server_method() #extern ssl
SSL_METHOD* TLS_client_method() #extern ssl

struct SSL_CTX {}

SSL_CTX* SSL_CTX_new(SSL_METHOD* meth) #extern ssl
SSL_CTX_free(SSL_CTX* ctx) #extern ssl

int SSL_CTX_load_verify_file(SSL_CTX* ctx, string CAfile) #extern ssl
int SSL_CTX_load_verify_dir(SSL_CTX* ctx, string CApath) #extern ssl
int SSL_CTX_load_verify_store(SSL_CTX* ctx, string CAstore) #extern ssl
int SSL_CTX_load_verify_locations(SSL_CTX* ctx, string CAfile, string CApath) #extern ssl

SSL_CTX_set_verify(SSL_CTX* ctx, int mode, void* callback) #extern ssl

struct SSL {}

SSL* SSL_new(SSL_CTX* ctx) #extern ssl
SSL_free(SSL* ssl) #extern ssl

u64 SSL_ctrl(SSL*ssl, int cmd, u64 larg, void *parg) #extern ssl

int SSL_set_fd(SSL* ssl, int fd) #extern ssl

int SSL_get_error(SSL* ssl, int ret) #extern ssl
int ERR_print_errors_cb(ERR_print_errors_cb_fn cb, void* u) #extern ssl

interface int ERR_print_errors_cb_fn(u8* str, u64 len, void* u)

int SSL_accept(SSL* ssl) #extern ssl
int SSL_stateless(SSL* ssl) #extern ssl
int SSL_connect(SSL* ssl) #extern ssl

int SSL_pending(SSL* ssl) #extern ssl
int SSL_has_pending(SSL* ssl) #extern ssl

int SSL_read(SSL* ssl, void* buf, int num) #extern ssl
int SSL_read_ex(SSL* ssl, void* buf, u64 num, u64* bytesread) #extern ssl

int SSL_peek(SSL* ssl, void *buf, int num) #extern ssl
int SSL_peek_ex(SSL* ssl, void *buf, u64 num, u64* bytesread) #extern ssl

int SSL_write(SSL* ssl, void* buf, int num) #extern ssl
int SSL_write_ex(SSL* ssl, void* buf, u64 num, u64* written) #extern ssl

int SSL_shutdown(SSL* s) #extern ssl


// Crypto
interface void* CRYPTO_malloc_fn(u64 num, u8* file, int line)
interface void* CRYPTO_realloc_fn(void* addr, u64 num, u8* file, int line)
interface CRYPTO_free_fn(void* addr, u8* file, int line)

int CRYPTO_set_mem_functions(CRYPTO_malloc_fn malloc_fn, CRYPTO_realloc_fn realloc_fn, CRYPTO_free_fn free_fn) #extern crypto


// Functions
enum OpenSSLMethod {
    Unknown;
    Client;
    Server;
}

SSL_CTX* initialize_openssl(OpenSSLMethod method, string cacert = empty_string) {
    ssl_method: SSL_METHOD*;
    switch method {
        case OpenSSLMethod.Client;
            ssl_method = TLS_client_method();
        case OpenSSLMethod.Server;
            ssl_method = TLS_server_method();
        default;
            ssl_method = TLS_method();
    }

    context := SSL_CTX_new(ssl_method);

    if !string_is_empty(cacert) {
        SSL_CTX_load_verify_file(context, cacert);
    }

    SSL_CTX_set_verify(context, 1, null);

    return context;
}

bool, SSL* connect_socket_openssl(Socket socket, string hostname, SSL_CTX* context) {
    success: bool;
    ssl := SSL_new(context);
    if ssl {
        fd_set: int;
        #if os == OS.Windows {
            fd := cast(u64, socket.socket);
            fd_set = SSL_set_fd(ssl, fd);
        }
        #if os == OS.Linux {
            fd_set = SSL_set_fd(ssl, socket.socket);
        }

        if fd_set == 1 {
            SSL_ctrl(ssl, /* SSL_CTRL_SET_TLSEXT_HOSTNAME */ 55, /* TLSEXT_NAMETYPE_host_name */ 0, hostname.data);

            result := SSL_connect(ssl);
            if result > 0 {
                success = true;
            }
            else {
                SSL_free(ssl);
            }
        }
        else {
            SSL_free(ssl);
        }
    }

    return success, ssl;
}
