struct Token {
    type: TokenType;
    value: string;
}

enum TokenType {
    Identifier;
    Struct;
    Enum;
    TypeDef;
    Star = '*';
    OpenParen = '(';
    CloseParen = ')';
    OpenBrace = '{';
    CloseBrace = '}';
    SemiColon = ';';
    Comma = ',';
}

LinkedList<Token*> get_file_tokens(string file) {
    initial_size := file.length;

    tokens: LinkedList<Token*>;

    i := 0;
    current_token: Token*;

    while i < initial_size {
        character := file[i++];

        if character == '#' {
            if current_token {
                print(current_token);
                add(&tokens, current_token);
                current_token = null;
            }

            eat_until_newline(&i, file);
            continue;
        }

        if character == ' ' || character == '\n' {
            if current_token {
                check_reserved_tokens(current_token);
                print(current_token);
                add(&tokens, current_token);
                current_token = null;
            }
            continue;
        }

        if current_token == null {
            token := get_token(character, i-1, file);
            if token.type == TokenType.Identifier {
                current_token = token;
            }
            else {
                print(token);
                add(&tokens, token);
            }
        }
        else {
            type := get_token_type(character);
            if type == TokenType.Identifier {
                current_token.value.length++;
            }
            else {
                check_reserved_tokens(current_token);
                print(current_token);
                add(&tokens, current_token);
                current_token = null;

                token := get_token(character, i-1, file);
                print(token);
                add(&tokens, token);
            }
        }
    }
    printf("%d\n", tokens.count);

    return tokens;
}

check_reserved_tokens(Token* token) {
    if token.type != TokenType.Identifier return;

    if token.value == "struct" token.type = TokenType.Struct;
    else if token.value == "enum" token.type = TokenType.Enum;
    else if token.value == "typedef" token.type = TokenType.TypeDef;
}

Token* get_token(u8 char, int i, string file) {
    token := new<Token>();

    token.type = get_token_type(char);
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
    if char == ';' return TokenType.SemiColon;
    if char == ',' return TokenType.Comma;

    return TokenType.Identifier;
}

eat_until_newline(int* i, string file) {
    index := *i;
    while file[index] != '\n' {
        index++;
    }

    *i = index + 1;
}

print(Token* token) {
    each i in 0..token.value.length-1 {
        putchar(token.value[i]);
    }
    putchar('\n');
}
putchar(u8 char) #extern "c"
