using System.Collections.Generic;
using System.Linq;

namespace Lang.Parsing
{
    public class ParseResult
    {
        public bool Success => !Errors.Any();
        public List<IAst> SyntaxTrees { get; } = new();
        public List<ParseError> Errors { get; } = new();
    }

    public interface IParser
    {
        ParseResult Parse(List<string> projectFiles);
    }

    public class Parser : IParser
    {
        private readonly ILexer _lexer;
        
        private class TokenEnumerator
        {
            private readonly List<Token> _tokens;
            private int _index;

            public TokenEnumerator(List<Token> tokens)
            {
                _tokens = tokens;
            }

            public Token Current { get; private set; }

            public bool MoveNext()
            {
                Current = _tokens.Count > _index ? _tokens[_index] : null;
                _index++;
                return Current != null;
            }

            public Token Peek(int steps = 0)
            {
                return _tokens.Count > _index + steps ? _tokens[_index + steps] : null;
            }

            public Token Last => _tokens[^1];
        }

        public Parser(ILexer lexer) => _lexer = lexer;

        public ParseResult Parse(List<string> projectFiles)
        {
            var parseResult = new ParseResult();

            foreach (var file in projectFiles)
            {
                var syntaxTrees = ParseFile(file, out var errors);
                if (errors.Any())
                {
                    foreach (var error in errors)
                    {
                        error.File = file;
                    }
                    parseResult.Errors.AddRange(errors);
                }
                else if (parseResult.Success)
                {
                    parseResult.SyntaxTrees.AddRange(syntaxTrees);
                }
            }

            return parseResult;
        }

        private List<IAst> ParseFile(string file, out List<ParseError> errors)
        {
            // 1. Load file tokens
            var tokens = _lexer.LoadFileTokens(file, out errors);

            // 2. Iterate through tokens, tracking different ASTs
            var syntaxTrees = new List<IAst>();
            var enumerator = new TokenEnumerator(tokens);
            while (enumerator.MoveNext())
            {
                var token = enumerator.Current!;
                switch (token!.Type)
                {
                    case TokenType.Token:
                        syntaxTrees.Add(ParseFunction(enumerator, errors));
                        break;
                    default:
                        errors.Add(new ParseError
                        {
                            Error = $"Unexpected token '{token.Value}'",
                            Token = enumerator.Current
                        });
                        break;
                }
            }

            return syntaxTrees;
        }

        private static FunctionAst ParseFunction(TokenEnumerator enumerator, List<ParseError> errors)
        {
            // 1. Determine return type and name of the function
            var function = new FunctionAst
            {
                ReturnType = ParseType(enumerator, errors),
            };
            enumerator.MoveNext();
            function.Name = enumerator.Current?.Value;

            // 2. Find open paren to start parsing arguments
            enumerator.MoveNext();
            if (enumerator.Current.Type != TokenType.OpenParen)
            {
                // Add an error to the function AST and continue until open paren
                errors.Add(new ParseError
                {
                    Error = $"Unexpected token '{enumerator.Current.Value}' in function definition",
                    Token = enumerator.Current
                });
                while (enumerator.Current != null && enumerator.Current.Type != TokenType.OpenParen)
                    enumerator.MoveNext();
            }

            // 3. Parse arguments until a close paren
            var commaRequiredBeforeNextArgument = false;
            Variable currentArgument = null;
            while (enumerator.MoveNext())
            {
                var token = enumerator.Current;

                if (token.Type == TokenType.CloseParen)
                {
                    break;
                }

                switch (token.Type)
                {
                    case TokenType.Token:
                        if (commaRequiredBeforeNextArgument)
                        {
                            errors.Add(new ParseError
                            {
                                Error = "Comma required after declaring an argument",
                                Token = token
                            });
                        }
                        else if (currentArgument == null)
                        {
                            currentArgument = new Variable {Type = ParseType(enumerator, errors)};
                        }
                        else
                        {
                            currentArgument.Name = token.Value;
                            function.Arguments.Add(currentArgument);
                            currentArgument = null;
                            commaRequiredBeforeNextArgument = true;
                        }
                        break;
                    case TokenType.Comma:
                        if (!commaRequiredBeforeNextArgument)
                        {
                            errors.Add(new ParseError
                            {
                                Error = "Unexpected comma in arguments",
                                Token = token
                            });
                        }
                        commaRequiredBeforeNextArgument = false;
                        break;
                    default:
                        errors.Add(new ParseError
                        {
                            Error = "Unexpected token in arguments",
                            Token = token
                        });
                        break;
                }
            }
            if (!commaRequiredBeforeNextArgument && function.Arguments.Any())
            {
                errors.Add(new ParseError
                {
                    Error = "Unexpected comma in arguments",
                    Token = enumerator.Current ?? enumerator.Last
                });
            }

            // 4. Find open brace to start parsing body
            enumerator.MoveNext();
            if (enumerator.Current?.Type != TokenType.OpenBrace)
            {
                // Add an error to the function AST and continue until open paren
                errors.Add(new ParseError
                {
                    Error = $"Unexpected token in function definition",
                    Token = enumerator.Current ?? enumerator.Last
                });
                while (enumerator.Current != null && enumerator.Current.Type != TokenType.OpenBrace)
                    enumerator.MoveNext();
            }

            // 5. Parse function body
            var closed = false;
            while (enumerator.MoveNext())
            {
                var token = enumerator.Current;

                if (token.Type == TokenType.CloseBrace)
                {
                    closed = true;
                    break;
                }

                var ast = ParseLine(enumerator, errors);
                if (ast != null)
                    function.Children.Add(ast);
            }
            if (!closed)
            {
                errors.Add(new ParseError
                {
                    Error = "Function not closed by '}'",
                    Token = enumerator.Current ?? enumerator.Last
                });
            }

            return function;
        }

        private static IAst ParseLine(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var token = enumerator.Current;

            switch (token?.Type)
            {
                case TokenType.Return:
                    return ParseReturn(enumerator, errors);
                case TokenType.Var:
                    return ParseDeclaration(enumerator, errors);
                case TokenType.If:
                    return ParseConditional(enumerator, errors);
                case TokenType.While:
                    return ParseWhile(enumerator, errors);
                case TokenType.Token:
                    var nextToken = enumerator.Peek();
                    switch (nextToken?.Type)
                    {
                        case TokenType.OpenParen:
                            return ParseCall(enumerator, errors);
                        case TokenType.Equals:
                            return ParseAssignment(enumerator, errors);
                        // TODO Handle other things
                        default:
                            errors.Add(new ParseError
                            {
                                Error = $"Unexpected token '{token.Value}'",
                                Token = token
                            });
                            return null;
                    }
                case TokenType.OpenBrace:
                    return ParseScope(enumerator, errors);
                case null:
                    errors.Add(new ParseError
                    {
                        Error = "End of file reached without closing scope",
                        Token = enumerator.Last
                    });
                    return null;
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token '{token.Value}'",
                        Token = token
                    });
                    return null;
            }
        }

        private static ScopeAst ParseScope(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var scopeAst = new ScopeAst();

            var closed = false;
            while (enumerator.MoveNext())
            {
                if (enumerator.Current.Type == TokenType.CloseBrace)
                {
                    closed = true;
                    break;
                }

                var ast = ParseLine(enumerator, errors);
                if (ast != null)
                    scopeAst.Children.Add(ast);
            }

            if (!closed)
            {
                errors.Add(new ParseError
                {
                    Error = "Scope not closed by '}'",
                    Token = enumerator.Current ?? enumerator.Last
                });
            }

            return scopeAst;
        }

        private static ConditionalAst ParseConditional(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var conditionalAst = new ConditionalAst();

            // 1. Parse the conditional expression by first iterating over the initial 'if'
            enumerator.MoveNext();
            conditionalAst.Condition = ParseExpression(enumerator, errors, TokenType.OpenBrace, TokenType.Then);

            // 2. Determine how many lines to parse
            switch (enumerator.Current?.Type)
            {
                case TokenType.Then:
                {
                    // Parse single AST
                    enumerator.MoveNext();
                    var ast = ParseLine(enumerator, errors);
                    if (ast != null)
                        conditionalAst.Children.Add(ast);
                    break;
                }
                case TokenType.OpenBrace:
                {
                    // Parse until close brace
                    while (enumerator.MoveNext())
                    {
                        var token = enumerator.Current;

                        if (token.Type == TokenType.CloseBrace)
                        {
                            break;
                        }

                        var ast = ParseLine(enumerator, errors);
                        if (ast != null)
                            conditionalAst.Children.Add(ast);
                    }
                    break;
                }
                case null:
                    errors.Add(new ParseError
                    {
                        Error = "Expected if to contain conditional expression",
                        Token = enumerator.Last
                    });
                    break;
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token '{enumerator.Current.Value}'",
                        Token = enumerator.Current
                    });
                    break;
            }

            // 3. Parse else block if necessary
            if (enumerator.Peek()?.Type == TokenType.Else)
            {
                // First clear the else and then determine how to parse else block
                enumerator.MoveNext();
                enumerator.MoveNext();
                switch (enumerator.Current?.Type)
                {
                    case TokenType.Then:
                    {
                        // Parse single AST
                        enumerator.MoveNext();
                        var ast = ParseLine(enumerator, errors);
                        if (ast != null)
                            conditionalAst.Else = ast;
                        break;
                    }
                    case TokenType.OpenBrace:
                    {
                        // Parse until close brace
                        conditionalAst.Else = ParseScope(enumerator, errors);
                        break;
                    }
                    case TokenType.If:
                        // Nest another conditional in else children
                        conditionalAst.Else = ParseConditional(enumerator, errors);
                        break;
                    case null:
                        errors.Add(new ParseError
                        {
                            Error = "Expected body of else branch",
                            Token = enumerator.Last
                        });
                        break;
                    default:
                        errors.Add(new ParseError
                        {
                            Error = $"Unexpected token '{enumerator.Current.Value}'",
                            Token = enumerator.Current
                        });
                        break;
                }
            }

            return conditionalAst;
        }

        private static WhileAst ParseWhile(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var whileAst = new WhileAst();

            // 1. Parse the conditional expression by first iterating over the initial 'while'
            enumerator.MoveNext();
            whileAst.Condition = ParseExpression(enumerator, errors, TokenType.OpenBrace, TokenType.Then);

            // 2. Determine how many lines to parse
            switch (enumerator.Current?.Type)
            {
                case TokenType.Then:
                {
                    // Parse single AST
                    enumerator.MoveNext();
                    var ast = ParseLine(enumerator, errors);
                    if (ast != null)
                        whileAst.Children.Add(ast);
                    break;
                }
                case TokenType.OpenBrace:
                {
                    // Parse until close brace
                    while (enumerator.MoveNext())
                    {
                        var token = enumerator.Current;

                        if (token.Type == TokenType.CloseBrace)
                        {
                            break;
                        }

                        var ast = ParseLine(enumerator, errors);
                        if (ast != null)
                            whileAst.Children.Add(ast);
                    }
                    break;
                }
                case null:
                    errors.Add(new ParseError
                    {
                        Error = "Expected while to contain conditional expression",
                        Token = enumerator.Last
                    });
                    break;
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token '{enumerator.Current.Value}'",
                        Token = enumerator.Current
                    });
                    break;
            }

            return whileAst;
        }

        private static DeclarationAst ParseDeclaration(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var declaration = new DeclarationAst();

            // 1. Expect to get variable name
            enumerator.MoveNext();
            switch (enumerator.Current?.Type)
            {
                case TokenType.Token:
                    declaration.Name = enumerator.Current.Value;
                    break;
                case TokenType.SemiColon:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token in declaration '{enumerator.Current.Value}'",
                        Token = enumerator.Current
                    });
                    return declaration;
                case null:
                    errors.Add(new ParseError
                    {
                        Error = "Expected variable to have name",
                        Token = enumerator.Last
                    });
                    break;
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token in declaration '{enumerator.Current.Value}'",
                        Token = enumerator.Current
                    });
                    break;
            }

            // 2. Expect to get equals sign
            enumerator.MoveNext();
            if (enumerator.Current?.Type != TokenType.Equals)
            {
                errors.Add(new ParseError
                {
                    Error = "Expected '=' in declaration'",
                    Token = enumerator.Current ?? enumerator.Last
                });
            }

            // 3. Parse expression, constant, or another token as the value
            declaration.Value = ParseRightHandSide(enumerator, errors);

            return declaration;
        }

        private static AssignmentAst ParseAssignment(TokenEnumerator enumerator, List<ParseError> errors)
        {
            // 1. Get the variable name
            var assignment = new AssignmentAst
            {
                Name = enumerator.Current.Value
            };

            // 2. Expect to get equals sign
            enumerator.MoveNext();
            if (enumerator.Current?.Type != TokenType.Equals)
            {
                errors.Add(new ParseError
                {
                    Error = "Expected '=' in declaration'",
                    Token = enumerator.Current ?? enumerator.Last
                });
            }

            // 3. Parse expression, constant, or another token as the value
            assignment.Value = ParseRightHandSide(enumerator, errors);

            return assignment;
        }

        private static IAst ParseRightHandSide(TokenEnumerator enumerator, List<ParseError> errors)
        {
            // Step over '=' sign
            enumerator.MoveNext();
            var token = enumerator.Current;

            if (token == null)
            {
                errors.Add(new ParseError
                {
                    Error = "Expected to have a value",
                    Token = enumerator.Last
                });
                return null;
            }

            // Constant or variable case
            if (enumerator.Peek()?.Type == TokenType.SemiColon)
            {
                enumerator.MoveNext();
                switch (token.Type)
                {
                    case TokenType.Number:
                    case TokenType.Boolean:
                    case TokenType.Literal:
                        var constant = new ConstantAst
                        {
                            Type = token.InferType(out var error),
                            Value = token.Value
                        };
                        if (error != null)
                            errors.Add(error);
                        return constant;
                    case TokenType.Token:
                        var variable = new VariableAst {Name = token.Value};
                        return variable;
                    default:
                        errors.Add(new ParseError
                        {
                            Error = $"Unexpected token '{token.Value}'",
                            Token = token
                        });
                        return null;
                }
            }

            // Expression case
            return ParseExpression(enumerator, errors);
        }

        private static ExpressionAst ParseExpression(TokenEnumerator enumerator, List<ParseError> errors, params TokenType[] endToken)
        {
            var expression = new ExpressionAst();
            if (!endToken.Any())
            {
                endToken = new[] {TokenType.SemiColon};
            }

            var operatorRequired = false;

            do
            {
                var token = enumerator.Current;

                if (endToken.Contains(token.Type))
                {
                    break;
                }

                if (operatorRequired)
                {
                    var op = token.ConvertOperator();
                    if (op != Operator.None)
                    {
                        expression.Operators.Add(op);
                        operatorRequired = false;
                    }
                    else
                    {
                        errors.Add(new ParseError
                        {
                            Error = $"Unexpected token '{token.Value}' when operator was expected",
                            Token = token
                        });
                        return null;
                    }
                }
                else
                {
                    var nextToken = enumerator.Peek();
                    switch (token.Type)
                    {
                        case TokenType.Number:
                        case TokenType.Boolean:
                        case TokenType.Literal:
                            // Parse constant or expression
                            expression.Children.Add(new ConstantAst
                            {
                                Type = token.InferType(out var error), Value = token.Value
                            });
                            if (error != null)
                                errors.Add(error);
                            operatorRequired = true;
                            break;
                        case TokenType.Token:
                            // Parse variable, call, or expression
                            switch (nextToken?.Type)
                            {
                                case TokenType.OpenParen:
                                    expression.Children.Add(ParseCall(enumerator, errors));
                                    break;
                                case null:
                                    errors.Add(new ParseError
                                    {
                                        Error = $"Expected token to follow '{token.Value}'",
                                        Token = token
                                    });
                                    break;
                                default:
                                    expression.Children.Add(new VariableAst {Name = token.Value});
                                    break;
                            }
                            operatorRequired = true;
                            break;
                        case TokenType.OpenParen:
                            if (enumerator.MoveNext())
                            {
                                expression.Children.Add(ParseExpression(enumerator, errors, TokenType.CloseParen));
                            }
                            else
                            {
                                errors.Add(new ParseError
                                {
                                    Error = $"Expected token to follow '{token.Value}'",
                                    Token = token
                                });
                            }
                            operatorRequired = true;
                            break;
                        default:
                            errors.Add(new ParseError
                            {
                                Error = $"Unexpected token '{token.Value}' in expression", Token = token
                            });
                            break;
                    }
                }

            } while (enumerator.MoveNext());

            if (!expression.Children.Any())
            {
                errors.Add(new ParseError
                {
                    Error = "Expression should contain elements",
                    Token = enumerator.Current ?? enumerator.Last
                });
            }
            else if (!operatorRequired && expression.Children.Any())
            {
                errors.Add(new ParseError
                {
                    Error = "Value required after operator",
                    Token = enumerator.Current ?? enumerator.Last
                });
            }

            return expression;
        }

        private static CallAst ParseCall(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var callAst = new CallAst
            {
                Function = enumerator.Current.Value
            };

            // This enumeration is the open paren
            enumerator.MoveNext();
            // Enumerate over the first argument
            var commaRequired = false;
            while (enumerator.MoveNext())
            {
                var token = enumerator.Current;

                if (enumerator.Current.Type == TokenType.CloseParen)
                {
                    break;
                }

                if (commaRequired)
                {
                    if (token.Type == TokenType.Comma)
                    {
                        commaRequired = false;
                    }
                    else
                    {
                        errors.Add(new ParseError
                        {
                            Error = "Expected comma before next argument",
                            Token = token
                        });
                    }
                }
                else
                {
                    var nextToken = enumerator.Peek();
                    switch (token.Type)
                    {
                        case TokenType.Number:
                        case TokenType.Boolean:
                        case TokenType.Literal:
                            // Parse constant or expression
                            switch (nextToken?.Type)
                            {
                                case TokenType.Comma:
                                case TokenType.CloseParen:
                                    var constant = new ConstantAst
                                    {
                                        Type = token.InferType(out var error),
                                        Value = token.Value
                                    };
                                    if (error != null)
                                        errors.Add(error);
                                    commaRequired = true;
                                    callAst.Arguments.Add(constant);
                                    break;
                                case null:
                                    errors.Add(new ParseError
                                    {
                                        Error = $"Expected to have a token following '{token.Value}'",
                                        Token = token
                                    });
                                    break;
                                default:
                                    callAst.Arguments.Add(ParseExpression(enumerator, errors, TokenType.Comma, TokenType.CloseParen));
                                    if (enumerator.Current.Type == TokenType.CloseParen)
                                    {
                                        // At this point, the call is complete, so return
                                        return callAst;
                                    }
                                    // Don't have to set commaRequired to false since ParseExpression has gone over it
                                    break;
                            }
                            break;
                        case TokenType.Token:
                            // Parse variable, call, or expression
                            switch (nextToken?.Type)
                            {
                                case TokenType.Comma:
                                case TokenType.CloseParen:
                                    callAst.Arguments.Add(new VariableAst {Name = token.Value});
                                    commaRequired = true;
                                    break;
                                case TokenType.OpenParen:
                                    callAst.Arguments.Add(ParseCall(enumerator, errors));
                                    commaRequired = true;
                                    break;
                                case null:
                                    errors.Add(new ParseError
                                    {
                                        Error = $"Expected to have a token following '{token.Value}'",
                                        Token = token
                                    });
                                    break;
                                default:
                                    callAst.Arguments.Add(ParseExpression(enumerator, errors, TokenType.Comma, TokenType.CloseParen));
                                    if (enumerator.Current.Type == TokenType.CloseParen)
                                    {
                                        // At this point, the call is complete, so return
                                        return callAst;
                                    }
                                    // Don't have to set commaRequired to false since ParseExpression has gone over it
                                    break;
                            }
                            break;
                        default:
                            errors.Add(new ParseError
                            {
                                Error = $"Unexpected token in '{token.Value}' function call '{callAst.Function}'",
                                Token = token
                            });
                            break;
                    }
                }
            }

            if (!commaRequired && callAst.Arguments.Any())
            {
                errors.Add(new ParseError
                {
                    Error = "Unexpected comma in call",
                    Token = enumerator.Current ?? enumerator.Last
                });
            }

            return callAst;
        }

        private static ReturnAst ParseReturn(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var returnAst = new ReturnAst();

            enumerator.MoveNext();
            var token = enumerator.Current;

            // Constant or variable case
            var nextToken = enumerator.Peek();
            switch (nextToken?.Type)
            {
                case TokenType.SemiColon:
                    switch (token.Type)
                    {
                        case TokenType.Number:
                        case TokenType.Boolean:
                        case TokenType.Literal:
                            returnAst.Value = new ConstantAst
                            {
                                Type = token.InferType(out var error),
                                Value = token.Value
                            };
                            if (error != null)
                                errors.Add(error);
                            break;
                        case TokenType.Token:
                            returnAst.Value = new VariableAst {Name = token.Value};
                            break;
                        default:
                            errors.Add(new ParseError
                            {
                                Error = $"Unexpected token '{token.Value}' in return statement",
                                Token = token
                            });
                            break;
                    }
                    enumerator.MoveNext();
                    break;
                case TokenType.OpenParen:
                    if (token.Type == TokenType.Token)
                    {
                        returnAst.Value = ParseCall(enumerator, errors);
                    }
                    else
                    {
                        errors.Add(new ParseError
                        {
                            Error = $"Unexpected token in '{token.Value}' in return statement",
                            Token = token
                        });
                    }
                    break;
                case null:
                    errors.Add(new ParseError
                    {
                        Error = "Return does not have value",
                        Token = token ?? enumerator.Last
                    });
                    break;
                default:
                    returnAst.Value = ParseExpression(enumerator, errors);
                    break;
            }

            return returnAst;
        }

        private static TypeDefinition ParseType(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var typeDefinition = new TypeDefinition
            {
                Type = enumerator.Current!.Value
            };

            // Determine whether to parse a generic type, otherwise return
            if (enumerator.Peek()?.Type == TokenType.LessThan)
            {
                // Clear the '<' before entering loop
                enumerator.MoveNext();
                var commaRequiredBeforeNextType = false;
                while (enumerator.MoveNext())
                {
                    var token = enumerator.Current;

                    if (token.Type == TokenType.GreaterThan)
                    {
                        if (!commaRequiredBeforeNextType && typeDefinition.Generics.Any())
                        {
                            errors.Add(new ParseError
                            {
                                Error = "Unexpected comma in type",
                                Token = new Token { Type = TokenType.Comma, Line = token.Line }
                            });
                        }
                        return typeDefinition;
                    }

                    if (!commaRequiredBeforeNextType)
                    {
                        switch (token.Type)
                        {
                            case TokenType.Token:
                                typeDefinition.Generics.Add(ParseType(enumerator, errors));
                                commaRequiredBeforeNextType = true;
                                break;
                            default:
                                errors.Add(new ParseError
                                {
                                    Error = "Unexpected token in type definition",
                                    Token = token
                                });
                                commaRequiredBeforeNextType = true;
                                break;
                        }
                    }
                    else
                    {
                        switch (token.Type)
                        {
                            case TokenType.Comma:
                                commaRequiredBeforeNextType = false;
                                break;
                            default:
                                errors.Add(new ParseError
                                {
                                    Error = "Unexpected token in type definition",
                                    Token = token
                                });
                                commaRequiredBeforeNextType = false;
                                break;
                        }
                    }
                }

                if (!typeDefinition.Generics.Any())
                {
                    errors.Add(new ParseError
                    {
                        Error = "Expected type to contain generics",
                        Token = enumerator.Current ?? enumerator.Last
                    });
                }
            }

            return typeDefinition;
        }
    }
}
