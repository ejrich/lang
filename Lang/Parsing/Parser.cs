﻿using System;
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
                    case TokenType.Struct:
                        syntaxTrees.Add(ParseStruct(enumerator, errors));
                        break;
                    default:
                        errors.Add(new ParseError
                        {
                            Error = $"Unexpected token '{token.Value}'", Token = enumerator.Current
                        });
                        break;
                }
            }

            return syntaxTrees;
        }

        private static FunctionAst ParseFunction(TokenEnumerator enumerator, List<ParseError> errors)
        {
            // 1. Determine return type and name of the function
            var function = new FunctionAst {ReturnType = ParseType(enumerator, errors)};
            enumerator.MoveNext();
            function.Name = enumerator.Current?.Value;

            // 2. Find open paren to start parsing arguments
            enumerator.MoveNext();
            if (enumerator.Current?.Type != TokenType.OpenParen)
            {
                // Add an error to the function AST and continue until open paren
                var token = enumerator.Current ?? enumerator.Last;
                errors.Add(new ParseError
                {
                    Error = $"Unexpected token '{token.Value}' in function definition",
                    Token = token
                });
                while (enumerator.Current != null && enumerator.Current.Type != TokenType.OpenParen)
                    enumerator.MoveNext();
            }

            // 3. Parse arguments until a close paren
            var commaRequiredBeforeNextArgument = false;
            Argument currentArgument = null;
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
                                Error = "Comma required after declaring an argument", Token = token
                            });
                        }
                        else if (currentArgument == null)
                        {
                            currentArgument = new Argument {Type = ParseType(enumerator, errors)};
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
                            errors.Add(new ParseError {Error = "Unexpected comma in arguments", Token = token});
                        }
                        commaRequiredBeforeNextArgument = false;
                        break;
                    default:
                        errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in arguments", Token = token});
                        break;
                }
            }

            if (!commaRequiredBeforeNextArgument && function.Arguments.Any())
            {
                errors.Add(new ParseError
                {
                    Error = "Unexpected comma in arguments", Token = enumerator.Current ?? enumerator.Last
                });
            }

            // 4. Find open brace to start parsing body
            enumerator.MoveNext();
            if (enumerator.Current?.Type != TokenType.OpenBrace)
            {
                // Add an error to the function AST and continue until open paren
                var token = enumerator.Current ?? enumerator.Last;
                errors.Add(new ParseError
                {
                    Error = $"Unexpected token '{token.Value}' in function definition",
                    Token = token
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
                    Error = "Function not closed by '}'", Token = enumerator.Current ?? enumerator.Last
                });
            }

            return function;
        }

        private static StructAst ParseStruct(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var structAst = new StructAst();

            // 1. Determine name of struct
            enumerator.MoveNext();
            switch (enumerator.Current?.Type)
            {
                case TokenType.Token:
                    structAst.Name = enumerator.Current.Value;
                    break;
                case null:
                    errors.Add(new ParseError {Error = "Expected struct to have name", Token = enumerator.Last});
                    break;
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token '{enumerator.Current.Value}' in struct definition", Token = enumerator.Current
                    });
                    break;
            }

            // 2. Parse over the open brace
            enumerator.MoveNext();
            if (enumerator.Current?.Type != TokenType.OpenBrace)
            {
                errors.Add(new ParseError
                {
                    Error = "Expected '{' token in struct definition", Token = enumerator.Current ?? enumerator.Last
                });
                while (enumerator.Current != null && enumerator.Current.Type != TokenType.OpenBrace)
                    enumerator.MoveNext();
            }

            // 3. Iterate through fields
            StructFieldAst currentField = null;
            var parsingFieldDefault = false;
            while (enumerator.MoveNext())
            {
                var token = enumerator.Current;

                if (token.Type == TokenType.CloseBrace)
                {
                    break;
                }

                switch (token.Type)
                {
                    case TokenType.Token:
                        // First get the type of the field
                        if (currentField == null)
                        {
                            currentField = new StructFieldAst {Type = ParseType(enumerator, errors)};
                        }
                        else if (currentField.Name == null)
                        {
                            currentField.Name = enumerator.Current.Value;
                        }
                        else
                        {
                            errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in struct", Token = token});
                        }
                        break;
                    case TokenType.SemiColon:
                        if (currentField != null)
                        {
                            // Catch if the name hasn't been set
                            if (currentField.Name == null || parsingFieldDefault)
                            {
                                errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in struct", Token = token});
                            }
                            // Add the field to the struct and continue
                            structAst.Fields.Add(currentField);
                            currentField = null;
                            parsingFieldDefault = false;
                        }
                        break;
                    case TokenType.Equals:
                        if (currentField?.Type != null && currentField.Name != null)
                        {
                            parsingFieldDefault = true;
                        }
                        else
                        {
                            errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in struct", Token = token});
                        }
                        break;
                    case TokenType.Number:
                    case TokenType.Boolean:
                    case TokenType.Literal:
                        if (currentField != null && parsingFieldDefault)
                        {
                            var constant = new ConstantAst {Type = InferType(token, out var error), Value = token.Value};
                            if (error != null)
                                errors.Add(error);
                            currentField.DefaultValue = constant;
                            parsingFieldDefault = false;
                        }
                        else
                        {
                            errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in struct", Token = token});
                        }
                        break;
                    default:
                        errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in struct", Token = token});
                        break;
                }
            }

            if (currentField != null)
            {
                var token = enumerator.Current ?? enumerator.Last;
                errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in struct", Token = token});
            }

            return structAst;
        }

        private static IAst ParseLine(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var token = enumerator.Current;

            switch (token?.Type)
            {
                case TokenType.Return:
                    return ParseReturn(enumerator, errors);
                case TokenType.If:
                    return ParseConditional(enumerator, errors);
                case TokenType.While:
                    return ParseWhile(enumerator, errors);
                case TokenType.Each:
                    return ParseEach(enumerator, errors);
                case TokenType.Token:
                    var nextToken = enumerator.Peek();
                    switch (nextToken?.Type)
                    {
                        case TokenType.OpenParen:
                            return ParseCall(enumerator, errors);
                        case TokenType.Colon:
                            return ParseDeclaration(enumerator, errors);
                        case TokenType.Equals:
                            return ParseAssignment(enumerator, errors);
                        case TokenType.Period:
                            return ParseStructFieldExpression(enumerator, errors);
                        // TODO Handle other things
                        default:
                            // Peek again for an '=', this is likely an operator assignment
                            if (enumerator.Peek(1)?.Type == TokenType.Equals)
                            {
                                return ParseAssignment(enumerator, errors);
                            }

                            return ParseExpression(enumerator, errors);
                    }
                case TokenType.Increment:
                case TokenType.Decrement:
                    return ParseExpression(enumerator, errors);
                case TokenType.OpenBrace:
                    return ParseScope(enumerator, errors);
                case null:
                    errors.Add(new ParseError
                    {
                        Error = "End of file reached without closing scope", Token = enumerator.Last
                    });
                    return null;
                default:
                    errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}'", Token = token});
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
                    Error = "Scope not closed by '}'", Token = enumerator.Current ?? enumerator.Last
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
                    conditionalAst.Children.Add(ParseScope(enumerator, errors));
                    break;
                }
                case null:
                    errors.Add(new ParseError
                    {
                        Error = "Expected if to contain conditional expression", Token = enumerator.Last
                    });
                    break;
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token '{enumerator.Current.Value}'", Token = enumerator.Current
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
                        errors.Add(new ParseError {Error = "Expected body of else branch", Token = enumerator.Last});
                        break;
                    default:
                        errors.Add(new ParseError
                        {
                            Error = $"Unexpected token '{enumerator.Current.Value}'", Token = enumerator.Current
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
                    whileAst.Children.Add(ParseScope(enumerator, errors));
                    break;
                }
                case null:
                    errors.Add(new ParseError {Error = "Expected while loop to contain body", Token = enumerator.Last});
                    break;
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token '{enumerator.Current.Value}'", Token = enumerator.Current
                    });
                    break;
            }

            return whileAst;
        }

        private static EachAst ParseEach(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var eachAst = new EachAst();

            // 1. Parse the iteration variable by first iterating over the initial 'each'
            enumerator.MoveNext();
            if (enumerator.Current?.Type == TokenType.Token)
            {
                eachAst.IterationVariable = enumerator.Current.Value;
            }
            else
            {
                errors.Add(new ParseError
                {
                    Error = "Expected variable in each block definition",
                    Token = enumerator.Current ?? enumerator.Last
                });
            }

            // 2. Parse over the in keyword
            enumerator.MoveNext();
            if (enumerator.Current?.Type != TokenType.In)
            {
                errors.Add(new ParseError
                {
                    Error = "Expected 'in' token in each block", Token = enumerator.Current ?? enumerator.Last
                });
                while (enumerator.Current != null && enumerator.Current.Type != TokenType.In)
                    enumerator.MoveNext();
            }

            // 3. Determine the iterator
            enumerator.MoveNext();
            var expression = ParseExpression(enumerator, errors, TokenType.OpenBrace, TokenType.Then, TokenType.Range);

            // 3a. Check if the next token is a range
            switch (enumerator.Current?.Type)
            {
                case TokenType.Range:
                    eachAst.RangeBegin = expression;
                    enumerator.MoveNext();
                    if (enumerator.Current == null)
                    {
                        errors.Add(new ParseError {Error = "Expected range to have an end", Token = enumerator.Last});
                        return eachAst;
                    }

                    eachAst.RangeEnd = ParseExpression(enumerator, errors, TokenType.OpenBrace, TokenType.Then);
                    if (enumerator.Current == null)
                    {
                        errors.Add(new ParseError
                        {
                            Error = "Expected each block to have iteration and body", Token = enumerator.Last
                        });
                        return eachAst;
                    }

                    break;
                case TokenType.OpenBrace:
                case TokenType.Then:
                    eachAst.Iteration = expression;
                    break;
                case null:
                    errors.Add(new ParseError
                    {
                        Error = "Expected each block to have iteration and body", Token = enumerator.Last
                    });
                    return eachAst;
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token '{enumerator.Current.Value}' in each block",
                        Token = enumerator.Current
                    });
                    return eachAst;
            }

            // 4. Determine how many lines to parse
            switch (enumerator.Current?.Type)
            {
                case TokenType.Then:
                {
                    // Parse single AST
                    enumerator.MoveNext();
                    var ast = ParseLine(enumerator, errors);
                    if (ast != null)
                        eachAst.Children.Add(ast);
                    break;
                }
                case TokenType.OpenBrace:
                {
                    // Parse until close brace
                    eachAst.Children.Add(ParseScope(enumerator, errors));
                    break;
                }
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token '{enumerator.Current.Value}'", Token = enumerator.Current
                    });
                    break;
            }

            return eachAst;
        }

        private static DeclarationAst ParseDeclaration(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var declaration = new DeclarationAst {Name = enumerator.Current.Value};

            // 1. Expect to get colon
            enumerator.MoveNext();
            if (enumerator.Current?.Type != TokenType.Colon)
            {
                var errorToken = enumerator.Current ?? enumerator.Last;
                errors.Add(new ParseError
                {
                    Error = $"Unexpected token in declaration '{errorToken.Value}'",
                    Token = errorToken
                });
                return declaration;
            }

            // 2. Check if type is given
            if (enumerator.Peek()?.Type == TokenType.Token)
            {
                enumerator.MoveNext();
                declaration.Type = ParseType(enumerator, errors);
            }

            // 3. Get the value or return
            enumerator.MoveNext();
            var token = enumerator.Current;
            switch (token?.Type)
            {
                case TokenType.Equals:
                    // Valid case
                    break;
                case TokenType.SemiColon:
                    if (declaration.Type == null)
                    {
                        errors.Add(new ParseError {Error = "Unexpected token declaration to have value", Token = token});
                    }
                    return declaration;
                case null:
                    errors.Add(new ParseError {Error = "Expected declaration to have value", Token = enumerator.Last});
                    return declaration;
                default:
                    errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in declaration", Token = token});
                    // Parse until there is an equals sign
                    while (enumerator.Current != null && enumerator.Current.Type != TokenType.Equals)
                        enumerator.MoveNext();
                    break;
            }
            
            // 4. Step over '=' sign
            if (!enumerator.MoveNext())
            {
                errors.Add(new ParseError {Error = "Expected declaration to have a value", Token = enumerator.Last});
                return null;
            }

            // 5. Parse expression, constant, or another token as the value
            declaration.Value = ParseExpression(enumerator, errors);

            return declaration;
        }

        private static AssignmentAst ParseAssignment(TokenEnumerator enumerator, List<ParseError> errors, IAst variable = null)
        {
            // 1. Set the variable
            var assignment = new AssignmentAst {Variable = variable ?? new VariableAst {Name = enumerator.Current.Value}};

            // 2. Expect to get equals sign
            if (!enumerator.MoveNext())
            {
                errors.Add(new ParseError {Error = "Expected '=' in assignment'", Token = enumerator.Last});
                return assignment;
            }

            // 3. Assign the operator is there is one
            var token = enumerator.Current;
            if (token.Type != TokenType.Equals)
            {
                var op = ConvertOperator(token);
                if (op != Operator.None)
                {
                    assignment.Operator = op;
                    if (enumerator.Peek()?.Type == TokenType.Equals)
                    {
                        enumerator.MoveNext();
                    }
                    else
                    {
                        errors.Add(new ParseError {Error = "Expected '=' in assignment'", Token = token});
                    }
                }
                else
                {
                    errors.Add(new ParseError {Error = "Expected operator in assignment", Token = token});
                }
            }

            // 4. Step over '=' sign
            if (!enumerator.MoveNext())
            {
                errors.Add(new ParseError {Error = "Expected to have a value", Token = enumerator.Last});
                return null;
            }

            // 5. Parse expression, constant, or another token as the value
            assignment.Value = ParseExpression(enumerator, errors);

            return assignment;
        }

        private static IAst ParseStructFieldExpression(TokenEnumerator enumerator, List<ParseError> errors)
        {
            // 1. Parse struct field until finished
            var structField = ParseStructField(enumerator, errors);

            // 2. Determine if expression or assignment
            var token = enumerator.Current;
            switch (token?.Type)
            {
                case TokenType.SemiColon:
                    return structField;
                case TokenType.Equals:
                    return ParseAssignment(enumerator, errors, structField);
                case null:
                    errors.Add(new ParseError {Error = "Expected value", Token = enumerator.Last});
                    return null;
                default:
                    if (enumerator.Peek()?.Type == TokenType.Equals)
                    {
                        return ParseAssignment(enumerator, errors, structField);
                    }
                    else
                    {
                        errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}'", Token = token});
                        return structField;
                    }
            }
        }

        private static StructFieldRefAst ParseStructField(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var structField = new StructFieldRefAst
            {
                Name = enumerator.Current.Value
            };

            if (enumerator.Peek()?.Type == TokenType.Period)
            {
                enumerator.MoveNext();
                if (enumerator.Peek()?.Type == TokenType.Token)
                {
                    enumerator.MoveNext();
                    structField.Value = ParseStructField(enumerator, errors);
                }
                else
                {
                    var token = enumerator.Current ?? enumerator.Last;
                    errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}'", Token = token});
                }
            }

            return structField;
        }

        private static IAst ParseExpression(TokenEnumerator enumerator, List<ParseError> errors,
            params TokenType[] endToken)
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
                    var op = ConvertOperator(token);
                    if (op != Operator.None)
                    {
                        if (op == Operator.Increment || op == Operator.Decrement)
                        {
                            // Create subexpression to hold the operation
                            // This case would be `var b = 4 + a++`, where we have a value before the operator
                            expression.Children[^1] = new ChangeByOneAst
                            {
                                Operator = op, Children = {expression.Children[^1]},
                            };
                        }
                        else
                        {
                            expression.Operators.Add(op);
                            operatorRequired = false;
                        }
                    }
                    else
                    {
                        errors.Add(new ParseError
                        {
                            Error = $"Unexpected token '{token.Value}' when operator was expected", Token = token
                        });
                        return null;
                    }
                }
                else
                {
                    var ast = ParseNextExpressionUnit(enumerator, errors, out operatorRequired);
                    if (ast != null)
                        expression.Children.Add(ast);
                }
            } while (enumerator.MoveNext());

            if (!expression.Children.Any())
            {
                errors.Add(new ParseError
                {
                    Error = "Expression should contain elements", Token = enumerator.Current ?? enumerator.Last
                });
            }
            else if (!operatorRequired && expression.Children.Any())
            {
                errors.Add(new ParseError
                {
                    Error = "Value required after operator", Token = enumerator.Current ?? enumerator.Last
                });
            }

            return expression.Children.Count == 1 ? expression.Children.First() : expression;
        }

        private static IAst ParseNextExpressionUnit(TokenEnumerator enumerator, List<ParseError> errors,
            out bool operatorRequired)
        {
            var token = enumerator.Current;
            var nextToken = enumerator.Peek();
            operatorRequired = true;
            switch (token.Type)
            {
                case TokenType.Number:
                case TokenType.Boolean:
                case TokenType.Literal:
                    // Parse constant or expression
                    var constant = new ConstantAst {Type = InferType(token, out var error), Value = token.Value};
                    if (error != null)
                        errors.Add(error);
                    return constant;
                case TokenType.Token:
                    // Parse variable, call, or expression
                    switch (nextToken?.Type)
                    {
                        case TokenType.OpenParen:
                            return ParseCall(enumerator, errors);
                        case TokenType.Period:
                            return ParseStructField(enumerator, errors);
                        case null:
                            errors.Add(new ParseError
                            {
                                Error = $"Expected token to follow '{token.Value}'", Token = token
                            });
                            return null;
                        default:
                            return new VariableAst {Name = token.Value};
                    }
                case TokenType.Increment:
                case TokenType.Decrement:
                    var op = ConvertOperator(token);
                    if (enumerator.MoveNext())
                    {
                        return new ChangeByOneAst
                        {
                            Prefix = true,
                            Operator = op,
                            Children = {ParseNextExpressionUnit(enumerator, errors, out operatorRequired)}
                        };
                    }
                    else
                    {
                        errors.Add(new ParseError {Error = $"Expected token to follow '{token.Value}'", Token = token});
                        return null;
                    }
                case TokenType.OpenParen:
                    if (enumerator.MoveNext())
                    {
                        return ParseExpression(enumerator, errors, TokenType.CloseParen);
                    }
                    else
                    {
                        errors.Add(new ParseError {Error = $"Expected token to follow '{token.Value}'", Token = token});
                        return null;
                    }
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token '{token.Value}' in expression", Token = token
                    });
                    operatorRequired = false;
                    return null;
            }
        }

        private static CallAst ParseCall(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var callAst = new CallAst {Function = enumerator.Current.Value};

            // This enumeration is the open paren
            enumerator.MoveNext();
            // Enumerate over the first argument
            while (enumerator.MoveNext())
            {
                var token = enumerator.Current;

                if (enumerator.Current.Type == TokenType.CloseParen)
                {
                    return callAst;
                }

                if (token.Type == TokenType.Comma)
                {
                    errors.Add(new ParseError {Error = "Expected comma before next argument", Token = token});
                }
                else
                {
                    callAst.Arguments.Add(ParseExpression(enumerator, errors, TokenType.Comma, TokenType.CloseParen));
                    
                    if (enumerator.Current?.Type == TokenType.CloseParen)
                    {
                        // At this point, the call is complete, so return
                        return callAst;
                    }
                }
            }

            errors.Add(new ParseError
            {
                Error = "Expected to close call", Token = enumerator.Last
            });

            return callAst;
        }

        private static ReturnAst ParseReturn(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var returnAst = new ReturnAst();

            if (enumerator.MoveNext())
            {
                if (enumerator.Current.Type != TokenType.SemiColon)
                {
                    returnAst.Value = ParseExpression(enumerator, errors);
                }
            }
            else
            {
                errors.Add(new ParseError {Error = "Return does not have value", Token = enumerator.Last});
            }

            return returnAst;
        }

        private static TypeDefinition ParseType(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var typeDefinition = new TypeDefinition {Name = enumerator.Current.Value};

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
                                Token = new Token {Type = TokenType.Comma, Line = token.Line}
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
                                    Error = "Unexpected token in type definition", Token = token
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
                                    Error = "Unexpected token in type definition", Token = token
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
                        Error = "Expected type to contain generics", Token = enumerator.Current ?? enumerator.Last
                    });
                }
            }

            return typeDefinition;
        }

        private static Type InferType(Token token, out ParseError error)
        {
            error = null;

            switch (token.Type)
            {
                case TokenType.Literal:
                    return Type.String;
                case TokenType.Number:
                    if (int.TryParse(token.Value, out _))
                    {
                        return Type.Int;
                    }
                    else if (float.TryParse(token.Value, out _))
                    {
                        return Type.Float;
                    }
                    else
                    {
                        return Type.Other;
                    }
                case TokenType.Boolean:
                    return Type.Boolean;
                // TODO This isn't right, but works for now
                case TokenType.Token:
                    return Type.Other;
                default:
                    return Type.Other;
            }
        }

        private static Operator ConvertOperator(Token token)
        {
            switch (token.Type)
            {
                // Multi-character operators
                case TokenType.And:
                    return Operator.And;
                case TokenType.Or:
                    return Operator.Or;
                case TokenType.Equality:
                    return Operator.Equality;
                case TokenType.Increment:
                    return Operator.Increment;
                case TokenType.Decrement:
                    return Operator.Decrement;
                // Handle single character operators
                default:
                    var op = (Operator)token.Value[0];
                    return Enum.IsDefined(typeof(Operator), op) ? op : Operator.None;
            }
        }
    }
}
