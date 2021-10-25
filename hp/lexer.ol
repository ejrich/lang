struct Token {
    type: TokenType;
}

enum TokenType {
    Identifier;
    Struct;
    Enum;
    TypeDef;
    Pound = '#';
    Star = '*';
    OpenParen = '(';
    CloseParen = ')';
    OpenBrace = '{';
    CloseBrace = '}';
    SemiColon = ';';
    Comma = ',';
}

get_file_tokens(string file) {
    initial_size := file.length;


}

Token* get_token() {
    token := new<Token>();

    return token;
}

eat_until_newline() {

}
