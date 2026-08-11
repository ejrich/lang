#import standard

// Module for working with network interfaces

// IP structs and helper functions

// v4
struct IPv4Header {
    version_and_length: u8;
    dscp_and_ecn: u8;
    total_length: u16;
    identification: u16;
    fragment: u16;
    time_to_live: u8;
    protocol: u8;
    checksum: u16;
    source_address: IPv4Address;
    destination_address: IPv4Address;
}

struct IPv4Address {
    a: u8;
    b: u8;
    c: u8;
    d: u8;
}

u8 get_ihl(IPv4Header* header) {
    assert(header != null);
    return header.version_and_length & 0xF;
}

u8 get_dscp(IPv4Header* header) {
    assert(header != null);
    return (header.dscp_and_ecn & 0xFC) >> 2;
}

u8 get_ecn(IPv4Header* header) {
    assert(header != null);
    return header.dscp_and_ecn & 0x3;
}

FragmentFlags, u16 get_fragment(IPv4Header* header) {
    assert(header != null);
    flags := cast(FragmentFlags, (header.fragment & 0xE000) >> 13);
    return flags, header.fragment & 0x1FFF;
}

[flags]
enum FragmentFlags: u8 {
    MoreFragments = 0x1;
    DontFragment  = 0x2;
}

// v6
struct IPv6Header {
    version_class_and_flow: u32;
    payload_length: u16;
    next_header: u8;
    hop_limit: u8;
    source_address: IPv6Address;
    destination_address: IPv6Address;
}

struct IPv6Address {
    a: u16;
    b: u16;
    c: u16;
    d: u16;
    e: u16;
    f: u16;
    g: u16;
    h: u16;
}

u8 get_traffic_class(IPv6Header* header) {
    assert(header != null);
    return (header.version_class_and_flow & 0x0FF00000) >> 20;
}

u32 get_flow_control(IPv6Header* header) {
    assert(header != null);
    return header.version_class_and_flow & 0x000FFFFF;
}

// TCP structs and helper functions

struct TCPHeader {
    source_port: u16;
    destination_port: u16;
    sequence_number: u32;
    acknowledgement_number: u32;
    offset_and_reserved: u8;
    flags: TCPFlags;
    window: u16;
    checksum: u16;
    urgent_pointer: u16;
}

[flags]
enum TCPFlags : u8 {
    FIN = 0x01;
    SYN = 0x02;
    RST = 0x04;
    PSH = 0x08;
    ACK = 0x10;
    URG = 0x20;
    ECE = 0x40;
    CWR = 0x80;
}

u8 get_data_offset(TCPHeader* header) {
    assert(header != null);
    return (header.offset_and_reserved & 0xF0) >> 4;
}

// UDP structs

struct UDPHeader {
    source_port: u16;
    destination_port: u16;
    length: u16;
    checksum: u16;
}

// TLS

struct TLSHeader {
    content_type: TLSContentType;
    major_version: u8;
    minor_version: u8;
    length_upper: u8;
    length_lower: u8;
}

enum TLSContentType : u8 {
    ChangeCipherSpec = 0x14;
    Alert            = 0x15;
    Handshake        = 0x16;
    Application      = 0x17;
    HeartBeat        = 0x18;
}

// HTTP

enum HTTPStatusCode : u16 {
    Unknown = 0;
    Continue = 100;
    SwitchingProtocols = 101;
    Processing = 102;
    EarlyHints = 103;
    Ok = 200;
    Created = 201;
    Accepted = 202;
    NonAuthoritativeInformation = 203;
    NoContent = 204;
    ResetContent = 205;
    PartialContent = 206;
    MultiStatus = 207;
    AlreadyReported = 208;
    MultipleChoices = 300;
    MovedPermanently = 301;
    Found = 302;
    SeeOther = 303;
    NotModified = 304;
    UseHttpPermanent = 305;
    TemporaryRedirect = 307;
    PermanentRedirect = 308;
    BadRequest = 400;
    Unauthorized = 401;
    PaymentRequired = 402;
    Forbidden = 403;
    NotFound = 404;
    MethodNotAllowed = 405;
    NotAcceptable = 406;
    ProxyAuthenticationRequired = 407;
    RequestTimeout = 408;
    Conflict = 409;
    Gone = 410;
    LengthRequired = 411;
    PreconditionFailed = 412;
    PayloadTooLarge = 413;
    UriTooLong = 414;
    UnsupportedMediaType = 415;
    RangeNotSatisfiable = 416;
    ExpectationFailed = 417;
    ImATeapot = 418;
    MisdirectedRequest = 421;
    UnprocessableEntity = 422;
    Locked = 423;
    FailedDependency = 424;
    TooEarly = 425;
    UpgradeRequired = 426;
    PreconditionRequired = 428;
    TooManyRequests = 429;
    RequestHeaderFieldsTooLarge = 431;
    UnavailableForLegalReasons = 451;
    InternalServerError = 500;
    NotImplemented = 501;
    BadGateway = 502;
    ServiceUnavailable = 503;
    GatewayTimeout = 504;
    HttpVersionNotSupported = 505;
    VariantAlsoNegotiates = 506;
    InsufficientStorage = 507;
    LoopDetected = 508;
    NotExtended = 510;
    NetworkAuthenticationRequired = 511;
}

enum HTTPVersion : u8 {
    HTTP1_0;
    HTTP1_1;
    HTTP2;
    HTTP3;
}

enum HTTPMethod : u8 {
    GET;
    HEAD;
    POST;
    PUT;
    DELETE;
    CONNECT;
    OPTIONS;
    TRACE;
    PATCH;
}

enum HTTPConnection : u8 {
    None;
    Close;
    KeepAlive;
}

struct HTTPHeader {
    name: string;
    value: string;
}

struct HTTPRequest {
    version: HTTPVersion;
    method: HTTPMethod;
    connection: HTTPConnection;
    headers: Array<HTTPHeader>;
    resource: string;
    host: string;
    accept: string;
    authorization: string;
    content_type: string;
    body: string;
}

string serialize_http_request(HTTPRequest request, Allocate allocator= default_allocator) {
    // Determine the buffer size before allocating
    length: u64;
    method := get_enum_name(request.method);
    length += method.length + 1;

    length += request.resource.length + 1;

    version := http_get_version_string(request.version);
    length += version.length + 2;

    each header in request.headers {
        length += header.name.length + header.value.length + 4;
    }

    connection_header := "Connection"; #const
    switch request.connection {
        case HTTPConnection.Close;     length += connection_header.length + 9;
        case HTTPConnection.KeepAlive; length += connection_header.length + 14;
    }

    host_header := "Host"; #const
    if !string_is_empty(request.host) {
        length += host_header.length + request.host.length + 4;
    }

    accept_header := "Accept"; #const
    if !string_is_empty(request.accept) {
        length += accept_header.length + request.accept.length + 4;
    }

    authorization_header := "Authorization"; #const
    if !string_is_empty(request.authorization) {
        length += authorization_header.length + request.authorization.length + 4;
    }

    content_type_header := "Content-Type"; #const
    if !string_is_empty(request.content_type) {
        length += content_type_header.length + request.content_type.length + 4;
    }

    content_length_header := "Content-Length"; #const
    content_length_factor: u64 = 1;
    if !string_is_empty(request.body) {
        content_length := request.body.length;
        digits := 0;
        while content_length {
            digits++;
            content_length /= 10;

            if content_length
                content_length_factor *= 10;
        }

        length += content_length_header.length + digits + 4;
        length += request.body.length;
    }

    length += 2;

    result: string = { length = length; data = allocator(length); }

    // Serialize the request into the result buffer
    memory_copy(result.data, method.data, method.length);
    i: u64 = method.length;
    result[i++] = ' ';

    memory_copy(result.data + i, request.resource.data, request.resource.length);
    i += request.resource.length;
    result[i++] = ' ';

    memory_copy(result.data + i, version.data, version.length);
    i += version.length;
    result[i++] = '\r';
    result[i++] = '\n';

    each header in request.headers {
        i = http_add_header(result, i, header.name, header.value);
    }

    switch request.connection {
        case HTTPConnection.Close;
            i = http_add_header(result, i, connection_header, "close");
        case HTTPConnection.KeepAlive;
            i = http_add_header(result, i, connection_header, "keep-alive");
    }

    if !string_is_empty(request.host) {
        i = http_add_header(result, i, host_header, request.host);
    }

    if !string_is_empty(request.accept) {
        i = http_add_header(result, i, accept_header, request.accept);
    }

    if !string_is_empty(request.authorization) {
        i = http_add_header(result, i, authorization_header, request.authorization);
    }

    if !string_is_empty(request.content_type) {
        i = http_add_header(result, i, content_type_header, request.content_type);
    }

    if !string_is_empty(request.body) {
        memory_copy(result.data + i, content_length_header.data, content_length_header.length);
        i += content_length_header.length;
        result[i++] = ':';
        result[i++] = ' ';

        content_length := request.body.length;
        while content_length_factor {
            digit := content_length / content_length_factor;
            result[i++] = digit + '0';

            content_length -= digit * content_length_factor;
            content_length_factor /= 10;
        }

        result[i++] = '\r';
        result[i++] = '\n';
    }

    result[i++] = '\r';
    result[i++] = '\n';

    if !string_is_empty(request.body) {
        memory_copy(result.data + i, request.body.data, request.body.length);
        i += request.body.length;
    }

    return result;
}


struct HTTPResponse {
    version: HTTPVersion;
    connection: HTTPConnection;
    status_code: HTTPStatusCode;
    headers: Array<HTTPHeader>;
    date: string;
    content_type: string;
    content_length: u64;
    body: string;
}

bool, u64, HTTPResponse parse_http_response(string text) {
    response: HTTPResponse;

    // Parse version
    i, value := http_parse_next_value(text, 0);
    if i >= text.length return false, 0, response;
    response.version = http_parse_version(value);

    // Parse status code
    i, value = http_parse_next_value(text, i + 1);
    if i >= text.length return false, 0, response;
    response.status_code = http_parse_status_code(value);

    i = http_clear_to_new_line(text, i);
    if i >= text.length return false, 0, response;

    // Parse headers
    header_found: bool;
    name: string;
    while true {
        header_found, i, name, value = http_parse_header_line(text, i);
        if !header_found break;

        if i >= text.length return false, 0, response;

        if name == "Connection" {
            if value == "close" {
                response.connection = HTTPConnection.Close;
            }
            else if value == "keep-alive" {
                response.connection = HTTPConnection.KeepAlive;
            }
        }
        else if name == "Date" {
            response.date = value;
        }
        else if name == "Content-Type" {
            response.content_type = value;
        }
        else if name == "Content-Length" {
            each j in value.length {
                response.content_length *= 10;
                response.content_length += value[j] - '0';
            }
        }
        else {
            // TODO Add others to headers array
        }
    }

    // Add the body
    if response.content_length > 0 {
        if i + response.content_length > text.length return false, 0, response;
        response.body = { length = response.content_length; data = text.data + i; }
        i += response.content_length;
    }

    return true, i, response;
}

// Socket functions

#if os == OS.Windows {
    struct AddressInfo {
        addr_info: ADDRINFOA*;
    }

    struct Socket {
        socket: SOCKET*;
    }
}
#if os == OS.Linux {
    struct AddressInfo {
        addr_info: addrinfo*;
    }

    struct Socket {
        socket: s32;
    }
}

bool, AddressInfo lookup_ip_address_tcp(string domain, string port) {
    success: bool;
    address_info: AddressInfo;

    #if os == OS.Windows {
        __init_wsa();
        hints: ADDRINFOA = {
            ai_family = AddressFamily.AF_UNSPEC;
            ai_socktype = SocketType.SOCK_STREAM;
            ai_protocol = SocketProtocol.IPPROTO_TCP;
        }

        success = getaddrinfo(domain, port, &hints, &address_info.addr_info) == 0;
    }
    #if os == OS.Linux {
        hints: addrinfo = {
            ai_family = SocketDomain.AF_UNSPEC;
            ai_socktype = SocketType.SOCK_STREAM;
            ai_protocol = SocketProtocol.IPPROTO_TCP;
        }

        success = getaddrinfo(domain, port, &hints, &address_info.addr_info) == 0;
    }

    return success, address_info;
}

close_ip_address(AddressInfo address_info) {
    #if os == OS.Windows {
        __init_wsa();
        freeaddrinfo(address_info.addr_info);
    }
    #if os == OS.Linux {
        freeaddrinfo(address_info.addr_info);
    }
}

bool, Socket connect_socket(AddressInfo address_info) {
    connected := false;
    socket: Socket;

    #if os == OS.Windows {
        __init_wsa();

        addr_info := address_info.addr_info;
        while (addr_info) {
            handle := socket(addr_info.ai_family, addr_info.ai_socktype, addr_info.ai_protocol);
            if cast(u64, handle) < 0xFFFFFFFFFFFFFFFF {
                connected = connect(handle, addr_info.ai_addr, addr_info.ai_addrlen) == 0;

                if connected {
                    socket.socket = handle;
                    break;
                }

                closesocket(handle);
            }
        }
    }
    #if os == OS.Linux {
        addr_info := address_info.addr_info;
        while (addr_info) {
            fd := socket(addr_info.ai_family, addr_info.ai_socktype, addr_info.ai_protocol);
            if fd > 0 {
                connected = connect(fd, addr_info.ai_addr, addr_info.ai_addrlen) == 0;

                if connected {
                    socket.socket = fd;
                    break;
                }

                close(fd);
            }
        }
    }

    return connected, socket;
}

s32 socket_send(Socket socket, void* buffer, s32 length) {
    assert(length >= 0);

    result: s32;
    #if os == OS.Windows {
        __init_wsa();
        result = send(socket.socket, buffer, length, 0);
    }
    #if os == OS.Linux {
        result = sendto(socket.socket, buffer, length, SocketMessageFlags.None, null, 0);
    }

    return result;
}

int socket_receive(Socket socket, void* buffer, u64 length) {
    assert(length >= 0);

    result: s32;
    #if os == OS.Windows {
        __init_wsa();
        result = recv(socket.socket, buffer, length, 0);
    }
    #if os == OS.Linux {
        result = recvfrom(socket.socket, buffer, length, SocketMessageFlags.None, null, null);
    }

    return result;
}

close_socket(Socket socket) {
    #if os == OS.Windows {
        __init_wsa();
        closesocket(socket.socket);
    }
    #if os == OS.Linux {
        close(socket.socket);
    }
}

#private

#if os == OS.Windows {
    #import atomic

    __init_wsa() {
        if !__wsa_startup_completed {
            if !__wsa_startup_initializing && compare_exchange(&__wsa_startup_initializing, true, false) == false {
                data: WSAData;
                WSAStartup(0x0202, &data);
                __wsa_startup_completed = true;
            }
            else {
                while !__wsa_startup_completed {
                    sleep(1);
                }
            }
        }
    }

    __wsa_startup_initializing := false;
    __wsa_startup_completed := false;
}

// HTTP Helper functions
u64, string http_parse_next_value(string text, u64 start) {
    i := start + 1;
    while i < text.length && text[i] != ' ' {
        i++;
    }

    value: string = { length = i - start; data = text.data + start; }
    return i, value;
}

u64 http_clear_to_new_line(string text, u64 i) {
    while i < text.length {
        if (text[i] == '\n' && i > 0 && text[i - 1] == '\r') {
            i++;
            break;
        }

        i++;
    }

    return i;
}

bool, u64, string, string http_parse_header_line(string text, u64 start) {
    // Handle the end of header \r\n
    if text[start] == '\r' && (start < text.length - 1) && text[start + 1] == '\n' {
        return false, start + 2, empty_string, empty_string;
    }

    i := start + 1;
    while i < text.length && text[i] != ':' {
        i++;
    }

    name: string = { length = i - start; data = text.data + start; }

    start = i + 2;
    i = http_clear_to_new_line(text, start);
    value: string = { length = i - 2 - start; data = text.data + start; }

    return true, i, name, value;
}

__http_1_0 := "HTTP/1.0"; #const
__http_1_1 := "HTTP/1.1"; #const
__http_2   := "HTTP/2"; #const
__http_3   := "HTTP/3"; #const

HTTPVersion http_parse_version(string name) {
    if name == __http_1_1 return HTTPVersion.HTTP1_1;
    if name == __http_2   return HTTPVersion.HTTP2;
    if name == __http_3   return HTTPVersion.HTTP3;
    return HTTPVersion.HTTP1_0;
}

string http_get_version_string(HTTPVersion version) {
    switch version {
        case HTTPVersion.HTTP1_1; return __http_1_1;
        case HTTPVersion.HTTP2;   return __http_2;
        case HTTPVersion.HTTP3;   return __http_3;
    }
    return __http_1_0;
}

HTTPStatusCode http_parse_status_code(string code) {
    number: u16;
    each i in code.length {
        number *= 10;
        number += code[i] - '0';
    }

    return cast(HTTPStatusCode, number);
}

u64 http_add_header(string result, u64 i, string name, string value) {
    memory_copy(result.data + i, name.data, name.length);
    i += name.length;
    result[i++] = ':';
    result[i++] = ' ';

    memory_copy(result.data + i, value.data, value.length);
    i += value.length;
    result[i++] = '\r';
    result[i++] = '\n';

    return i;
}
