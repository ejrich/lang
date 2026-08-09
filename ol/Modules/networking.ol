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
    #import standard

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
