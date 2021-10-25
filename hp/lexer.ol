struct Token {
    type: TokenType;
    value: string;
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

    tokens: LinkedList<Token*>;
    add(&tokens, null);
}

Token* get_token() {
    token := new<Token>();

    return token;
}

eat_until_newline() {

}
