struct Token {
    type: TokenType;
    value: string;
}

enum TokenType {
    Identifier;
    Struct;
    Union;
    Enum;
    Typedef;
    Extern;
    Signed;
    Unsigned;
    Long;
    Int;
    Short;
    Char;
    Float;
    Double;
    Const;
    Static;
    Attribute;
    Extension;
    Star = '*';
    OpenParen = '(';
    CloseParen = ')';
    OpenBrace = '{';
    CloseBrace = '}';
    OpenBracket = '[';
    CloseBracket = ']';
    SemiColon = ';';
    Comma = ',';
}

LinkedList<Token> get_file_tokens(string file) {
    initial_size := file.length;

    tokens: LinkedList<Token>;

    i := 0;
    current := false;
    current_token: Token;

    while i < initial_size {
        character := file[i];

        if character == '#' {
            if current {
                add(&tokens, current_token);
                current = false;
            }

            eat_until_newline(&i, file);
        }
        else if character == ' ' || character == '\n' {
            if current {
                check_reserved_tokens(&current_token);
                add(&tokens, current_token);
                current = false;
            }
        }
        else {
            if !current {
                token := get_token(character, i, file);
                if token.type == TokenType.Identifier {
                    current_token = token;
                    current = true;
                }
                else {
                    add(&tokens, token);
                }
            }
            else {
                type := get_token_type(character);
                if type == TokenType.Identifier {
                    current_token.value.length++;
                }
                else {
                    check_reserved_tokens(&current_token);
                    add(&tokens, current_token);
                    current = false;

                    token := get_token(character, i, file);
                    add(&tokens, token);
                }
            }
        }
        i++;
    }

    return tokens;
}

check_reserved_tokens(Token* token) {
    if token.type != TokenType.Identifier return;

    if token.value == "struct" token.type = TokenType.Struct;
    else if token.value == "union" token.type = TokenType.Union;
    else if token.value == "enum" token.type = TokenType.Enum;
    else if token.value == "typedef" token.type = TokenType.Typedef;
    else if token.value == "extern" token.type = TokenType.Extern;
    else if token.value == "signed" token.type = TokenType.Signed;
    else if token.value == "unsigned" token.type = TokenType.Unsigned;
    else if token.value == "long" token.type = TokenType.Long;
    else if token.value == "int" token.type = TokenType.Int;
    else if token.value == "short" token.type = TokenType.Short;
    else if token.value == "char" token.type = TokenType.Char;
    else if token.value == "float" token.type = TokenType.Float;
    else if token.value == "double" token.type = TokenType.Double;
    else if token.value == "const" token.type = TokenType.Const;
    else if token.value == "static" token.type = TokenType.Static;
    else if token.value == "__extension__" token.type = TokenType.Extension;
    else if token.value == "__attribute__" token.type = TokenType.Attribute;
}

Token get_token(u8 char, int i, string file) {
    token: Token = { type = get_token_type(char); }

    token.value.length = 1;
    token.value.data = file.data + i;

    return token;
}

TokenType get_token_type(u8 char) {
    if char == '*' return TokenType.Star;
    if char == '(' return TokenType.OpenParen;
    if char == ')' return TokenType.CloseParen;
    if char == '{' return TokenType.OpenBrace;
    if char == '}' return TokenType.CloseBrace;
    if char == '[' return TokenType.OpenBracket;
    if char == ']' return TokenType.CloseBracket;
    if char == ';' return TokenType.SemiColon;
    if char == ',' return TokenType.Comma;

    return TokenType.Identifier;
}

eat_until_newline(int* i, string file) {
    index := *i;
    while file[index] != '\n' {
        index++;
    }

    *i = index;
}
