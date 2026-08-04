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
    return (header.version_class_and_flow & 0x000FFFFF;
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
