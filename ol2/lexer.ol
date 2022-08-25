init_lexer() {
    invalid_identifier_characters['(']  = true;
    invalid_identifier_characters[')']  = true;
    invalid_identifier_characters['[']  = true;
    invalid_identifier_characters[']']  = true;
    invalid_identifier_characters['{']  = true;
    invalid_identifier_characters['}']  = true;
    invalid_identifier_characters['!']  = true;
    invalid_identifier_characters['&']  = true;
    invalid_identifier_characters['|']  = true;
    invalid_identifier_characters['^']  = true;
    invalid_identifier_characters['+']  = true;
    invalid_identifier_characters['-']  = true;
    invalid_identifier_characters['*']  = true;
    invalid_identifier_characters['/']  = true;
    invalid_identifier_characters['%']  = true;
    invalid_identifier_characters['=']  = true;
    invalid_identifier_characters[':']  = true;
    invalid_identifier_characters[';']  = true;
    invalid_identifier_characters['"']  = true;
    invalid_identifier_characters['\''] = true;
    invalid_identifier_characters['<']  = true;
    invalid_identifier_characters['>']  = true;
    invalid_identifier_characters[',']  = true;
    invalid_identifier_characters['.']  = true;
    invalid_identifier_characters['#']  = true;
    invalid_identifier_characters['\\'] = true;

    escaped_characters['\''] = "\'";
    escaped_characters['"']  = "\"";
    escaped_characters['\\'] = "\\";
    escaped_characters['a']  = "\a";
    escaped_characters['b']  = "\b";
    escaped_characters['f']  = "\f";
    escaped_characters['n']  = "\n";
    escaped_characters['r']  = "\r";
    escaped_characters['t']  = "\t";
    escaped_characters['v']  = "\v";
    escaped_characters['0']  = "\0";
}

struct Token {
    type: TokenType;
    flags: TokenFlags;
    value: string;
    line: u32;
    column: u32;
}

Array<Token> load_file_tokens(string file_path, int file_index) {
    found, file_text := read_file(file_path, allocate);

    token_list: TokenList;
    if !found {
        report_error_message("File not found '%'", file_path);
        return token_list.tokens;
    }

    estimated_tokens := file_text.length / 2;
    token_list.allocated_length = estimated_tokens;
    token_list.tokens.data = allocate(estimated_tokens * size_of(Token));

    parse_tokens(file_text, file_index, &token_list);

    return token_list.tokens;
}

parse_tokens(string file_text, int file_index, TokenList* token_list) {
    line: u32 = 1;
    column: u32;
    i := 0;
    while i < file_text.length {
        token: Token;
        character := file_text[i];
        column++;

        switch character {
            case '\n'; {
                line++;
                column = 0;
            }
            case ' ';
            case '\r';
            case '\t'; {}
            // Handle possible comments
            case '/'; {
                character = file_text[i+1];
                if character == '/' {
                    i++;
                    line++;
                    column = 0;
                    while file_text[++i] != '\n' {}
                }
                else if character == '*' {
                    i += 2;
                    column += 2;
                    while i < file_text.length {
                        character = file_text[i++];
                        if character == '*' {
                            character = file_text[i];
                            if character == '/' {
                                column++;
                                break;
                            }
                            else if character == '\n' {
                                line++;
                                column = 1;
                            }
                            else column++;
                            i++;
                        }
                        else if character == '\n' {
                            line++;
                            column = 1;
                        }
                        else column++;
                    }
                }
                else
                {
                    token = { type = TokenType.ForwardSlash; value = "/"; line = line; column = column; }
                    add_token(token_list, token);
                }
            }
            // Handle literals
            case '"'; {
                literal_escape_token := false;
                has_escaped_tokens := false;
                start_index := i + 1;
                error := false;
                token = { type = TokenType.Literal; value = ""; line = line; column = column; }
                string_buffer: StringBuffer;

                while i < file_text.length - 1 {
                    character = file_text[++i];
                    column++;

                    if character == '\\' && !literal_escape_token {
                        has_escaped_tokens = true;
                        literal_escape_token = true;
                    }
                    else if character == '\n' {
                        line++;
                        column = 0;
                        if literal_escape_token {
                            error = true;
                            literal_escape_token = false;
                        }
                    }
                    else if literal_escape_token {
                        memory_copy(&string_buffer.buffer + string_buffer.length, file_text.data + start_index, i - start_index - 1);
                        string_buffer.length += i - start_index - 1;

                        char_found, escaped_character := get_escaped_character(character);
                        if char_found {
                            string_buffer.buffer[string_buffer.length++] = escaped_character[0];
                        }
                        else error = true;
                        start_index = i + 1;
                        literal_escape_token = false;
                    }
                    else if character == '"' {
                        if has_escaped_tokens {
                            if i > start_index {
                                memory_copy(&string_buffer.buffer + string_buffer.length, file_text.data + start_index, i - start_index);
                                string_buffer.length += i - start_index;
                            }

                            token.value = { length = string_buffer.length; data = allocate(string_buffer.length); }
                            memory_copy(token.value.data, &string_buffer.buffer, string_buffer.length);
                        }
                        else {
                            token.value = substring(file_text, start_index, i - start_index);
                        }

                        if error report_error("Unexpected token '%'", file_index, token, token.value);

                        add_token(token_list, token);
                        break;
                    }
                }
            }
            // Handle characters
            case '\''; {
                character = file_text[++i];
                if character == '\\' {
                    character = file_text[++i];
                    char_found, escaped_character := get_escaped_character(character);
                    if char_found {
                        token = { type = TokenType.Character; value = escaped_character; line = line; column = column; }
                        add_token(token_list, token);
                    }
                    else report_error("Unknown escaped character '\\%'", file_index, line, column, character);
                    column += 2;
                }
                else {
                    column++;
                    token = { type = TokenType.Character; value = substring(file_text, i, 1); line = line; column = column; }
                    add_token(token_list, token);
                }

                character = file_text[i+1];
                if character == '\'' {
                    i++;
                    column++;
                }
                else report_error("Expected a single digit character", file_index, line, column);
            }
            // Handle ranges and varargs
            case '.'; {
                token = { line = line; column = column; }

                if file_text[i+1] == '.' {
                    if file_text[i+2] == '.' {
                        token = { type = TokenType.VarArgs; value = "..."; }
                        i += 2;
                        column += 2;
                    }
                    else {
                        token.type = TokenType.Range;
                        token.value = "..";
                        i++;
                        column++;
                    }
                }
                else token = { type = TokenType.Period; value = "."; }

                add_token(token_list, token);
            }
            case '!'; {
                token = { line = line; column = column; }

                if file_text[i+1] == '=' {
                    token = { type = TokenType.NotEqual; value = "!="; }
                    i++;
                    column++;
                }
                else token = { type = TokenType.Not; value = "!"; }

                add_token(token_list, token);
            }
            case '&'; {
                token = { line = line; column = column; }

                if file_text[i+1] == '&' {
                    token = { type = TokenType.And; value = "&&"; }
                    i++;
                    column++;
                }
                else token = { type = TokenType.Ampersand; value = "&"; }

                add_token(token_list, token);
            }
            case '|'; {
                token = { line = line; column = column; }

                if file_text[i+1] == '|' {
                    token = { type = TokenType.Or; value = "||"; }
                    i++;
                    column++;
                }
                else token = { type = TokenType.Pipe; value = "|"; }

                add_token(token_list, token);
            }
            case '+'; {
                token = { line = line; column = column; }

                if file_text[i+1] == '+' {
                    token = { type = TokenType.Increment; value = "++"; }
                    i++;
                    column++;
                }
                else token = { type = TokenType.Plus; value = "+"; }

                add_token(token_list, token);
            }
            case '-'; {
                token = { line = line; column = column; }

                character = file_text[i+1];
                if character == '-' {
                    token = { type = TokenType.Decrement; value = "--"; }
                    i++;
                    column++;
                }
                else if character >= '0' && character <= '9' {
                    token.type = TokenType.Number;
                    start_index := i++;
                    offset := 1;

                    while i < file_text.length - 1 {
                        character = file_text[i+1];

                        if character < '0' || character > '9' {
                            if character == '.' {
                                if (token.flags & TokenFlags.Float) == TokenFlags.Float || file_text[i+2] == '.'
                                    break;
                                else
                                    token.flags |= TokenFlags.Float;
                            }
                            else {
                                if character == '\n' {
                                    i++;
                                    line++;
                                    column = 0;
                                    offset = 0;
                                }
                                else if character == ' ' || character == '\r' || character == '\t' {
                                    i++;
                                    column++;
                                    offset = 0;
                                }
                                break;
                            }
                        }

                        i++;
                        column++;
                    }

                    token.value = substring(file_text, start_index, i - start_index + offset);
                }
                else token = { type = TokenType.Minus; value = "-"; }

                add_token(token_list, token);
            }
            case '='; {
                token = { line = line; column = column; }

                if file_text[i+1] == '=' {
                    token = { type = TokenType.Equality; value = "=="; }
                    i++;
                    column++;
                }
                else token = { type = TokenType.Equals; value = "="; }

                add_token(token_list, token);
            }
            case '<'; {
                token = { line = line; column = column; }

                character = file_text[i+1];
                if character == '=' {
                    token = { type = TokenType.LessThanEqual; value = "<="; }
                    i++;
                    column++;
                }
                else if character == '<' {
                    if file_text[i+2] == '<' {
                        token = { type = TokenType.RotateLeft; value = "<<<"; }
                        i += 2;
                        column += 2;
                    }
                    else {
                        token = { type = TokenType.ShiftLeft; value = "<<"; }
                        i++;
                        column++;
                    }
                }
                else token = { type = TokenType.LessThan; value = "<"; }

                add_token(token_list, token);
            }
            case '>'; {
                token = { line = line; column = column; }

                character = file_text[i+1];
                if character == '=' {
                    token = { type = TokenType.GreaterThanEqual; value = ">="; }
                    i++;
                    column++;
                }
                else if character == '>' {
                    if file_text[i+2] == '>' {
                        token = { type = TokenType.RotateRight; value = ">>>"; }
                        i += 2;
                        column += 2;
                    }
                    else {
                        token = { type = TokenType.ShiftRight; value = ">>"; }
                        i++;
                        column++;
                    }
                }
                else token = { type = TokenType.GreaterThan; value = ">"; }

                add_token(token_list, token);
            }
            case '(';
            case ')';
            case '[';
            case ']';
            case '{';
            case '}';
            case '^';
            case '*';
            case '%';
            case ':';
            case ';';
            case ',';
            case '#'; {
                token = { type = cast(TokenType, character); value = substring(file_text, i, 1); line = line; column = column; }
                add_token(token_list, token);
            }
            // Handle numbers
            case '0';
            case '1';
            case '2';
            case '3';
            case '4';
            case '5';
            case '6';
            case '7';
            case '8';
            case '9'; {
                start_index := i;
                offset := 1;
                token = { type = TokenType.Number; line = line; column = column; }

                while i < file_text.length - 1 {
                    next_character := file_text[i+1];

                    if next_character < '0' || next_character > '9' {
                        if next_character == '.' {
                            if (token.flags & TokenFlags.Float) == TokenFlags.Float || file_text[i+2] == '.'
                                break;
                            else
                                token.flags |= TokenFlags.Float;
                        }
                        else if next_character == 'x' {
                            if i == start_index && character == '0'
                                token.flags |= TokenFlags.HexNumber;
                            else
                                break;
                        }
                        else if next_character == '\n' {
                            i++;
                            line++;
                            column = 0;
                            offset = 0;
                            break;
                        }
                        else if next_character == ' ' || next_character == '\r' || next_character == '\t' {
                            i++;
                            column++;
                            offset = 0;
                            break;
                        }
                        else if (token.flags & TokenFlags.HexNumber) != TokenFlags.HexNumber || !is_hex_letter(next_character)
                            break;
                    }

                    i++;
                    column++;
                }

                token.value = substring(file_text, start_index, i - start_index + offset);
                add_token(token_list, token);
            }
            // Handle other identifiers
            default; {
                start_index := i;
                offset := 1;
                token = { line = line; column = column; }

                while i < file_text.length - 1 {
                    character = file_text[i+1];
                    if character == '\n' {
                        i++;
                        line++;
                        column = 0;
                        offset = 0;
                        break;
                    }
                    else if character == ' ' || character == '\r' || character == '\t' {
                        i++;
                        column++;
                        offset = 0;
                        break;
                    }
                    else if is_not_identifier_character(character)
                        break;

                    i++;
                    column++;
                }

                token.value = substring(file_text, start_index, i - start_index + offset);

                switch token.value.length {
                    case 2; {
                        if token.value == "if" token.type = TokenType.If;
                        else if token.value == "in" token.type = TokenType.In;
                    }
                    case 3; {
                        if token.value == "asm" token.type = TokenType.Asm;
                        else if token.value == "out" token.type = TokenType.Out;
                    }
                    case 4; {
                        if token.value == "case" token.type = TokenType.Case;
                        else if token.value == "cast" token.type = TokenType.Cast;
                        else if token.value == "each" token.type = TokenType.Each;
                        else if token.value == "else" token.type = TokenType.Else;
                        else if token.value == "enum" token.type = TokenType.Enum;
                        else if token.value == "null" token.type = TokenType.Null;
                        else if token.value == "true" token.type = TokenType.Boolean;
                    }
                    case 5; {
                        if token.value == "break" token.type = TokenType.Break;
                        else if token.value == "false" token.type = TokenType.Boolean;
                        else if token.value == "while" token.type = TokenType.While;
                        else if token.value == "union" token.type = TokenType.Union;
                        else if token.value == "defer" token.type = TokenType.Defer;
                    }
                    case 6; {
                        if token.value == "return" token.type = TokenType.Return;
                        else if token.value == "struct" token.type = TokenType.Struct;
                        else if token.value == "switch" token.type = TokenType.Switch;
                    }
                    case 7; {
                        if token.value == "default" token.type = TokenType.Default;
                    }
                    case 8; {
                        if token.value == "continue" token.type = TokenType.Continue;
                        else if token.value == "operator" token.type = TokenType.Operator;
                    }
                    case 9; {
                        if token.value == "interface" token.type = TokenType.Interface;
                    }
                }

                add_token(token_list, token);
            }
        }
        i++;
    }
}

string substring(string str, int start, int length) {
    result: string = { length = length; data = str.data + start; }
    return result;
}

enum TokenType {
    Identifier;
    Number;
    Boolean;
    Literal;
    Character;
    Struct;
    Enum;
    Union;
    Return;
    If;
    Else;
    While;
    Each;
    And;
    Or;
    Equality;
    NotEqual;
    Increment;
    Decrement;
    GreaterThanEqual;
    LessThanEqual;
    In;
    Range;
    Null;
    Cast;
    ShiftLeft;
    ShiftRight;
    RotateLeft;
    RotateRight;
    Operator;
    Break;
    Continue;           // 32
    Not = '!';          // 33
    Pound = '#';        // 35
    Percent = '%';      // 37
    Ampersand = '&';    // 38
    OpenParen = '(';    // 40
    CloseParen = ')';   // 41
    Asterisk = '*';     // 42
    Plus = '+';         // 43
    Comma = ',';        // 44
    Minus = '-';        // 45
    Period = '.';       // 46
    ForwardSlash = '/'; // 47
    Colon = ':';        // 58
    SemiColon = ';';    // 59
    LessThan = '<';     // 60
    Equals = '=';       // 61
    GreaterThan = '>';  // 62
    OpenBracket = '[';  // 91
    CloseBracket = ']'; // 93
    Caret = '^';        // 94
    OpenBrace = '{';    // 123
    Pipe = '|';         // 124
    CloseBrace = '}';   // 125
    VarArgs = 256;
    Interface;
    Out;
    Asm;
    Switch;
    Case;
    Default;
    Defer;
}

[flags]
enum TokenFlags {
    None = 0;
    Float = 1;
    HexNumber = 2;
}

struct TokenList {
    allocated_length: int;
    tokens: Array<Token>;
}

#private

add_token(TokenList* token_list, Token token) {
    if token_list.tokens.length > token_list.allocated_length {
        new_allocated_length := token_list.allocated_length + 20;
        token_list.tokens.data = reallocate(token_list.tokens.data, token_list.allocated_length * size_of(Token), new_allocated_length * size_of(Token));
        token_list.allocated_length = new_allocated_length;
    }

    token_list.tokens[token_list.tokens.length++] = token;
}

bool is_hex_letter(u8 character) #inline {
    if character >= 'A' && character <= 'F' return true;
    else if character >= 'a' && character <= 'f' return true;
    return false;
}

bool is_not_identifier_character(u8 character) #inline {
    return invalid_identifier_characters[character];
}

bool, string get_escaped_character(u8 character) #inline {
    value := escaped_characters[character];

    return !string_is_empty(value), value;
}

invalid_identifier_characters: Array<bool>[256];
escaped_characters: Array<string>[256];
