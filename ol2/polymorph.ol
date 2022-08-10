StructAst* create_polymorphed_struct(StructAst* base, string name, TypeKind type_kind, bool private_generic_types, Params<TypeAst*> generic_types) {
    poly_struct: StructAst = {
        ast_type = AstType.Struct; flags = AstFlags.IsType; file_index = base.file_index; line = base.line; column = base.column; name = name;
        type_kind = type_kind; attributes = base.attributes; base_struct_name = base.name; base_type_definition = base.base_type_definition;
        base_struct = base.base_struct; generic_types = allocate_array(generic_types);
    }

    if private_generic_types poly_struct.flags |= AstFlags.Private;
    array_resize(&poly_struct.fields, base.fields.length, allocate, reallocate);

    each field, i in base.fields {
        if field.flags & AstFlags.HasGenerics {
            new_field := copy_ast(field);
            new_field.type_definition = copy_type(field.type_definition, generic_types);
            new_field.name = field.name;
            new_field.value = field.value;
            new_field.assignments = field.assignments;
            new_field.array_values = field.array_values;
            poly_struct.fields[i] = new_field;
        }
        else
            poly_struct.fields[i] = field;
    }

    pointer := new<StructAst>();
    *pointer = poly_struct;

    return pointer;
}

FunctionAst* create_polymorphed_function(FunctionAst* base, string name, bool private_generic_types, Array<TypeAst*> generic_types) {
    function := copy_ast(base);
    function.function_flags = base.function_flags;
    function.name = name;
    if private_generic_types || (base.flags & AstFlags.Private) == AstFlags.Private
        function.flags |= AstFlags.Private;

    if function.function_flags & FunctionFlags.ReturnTypeHasGenerics
        function.return_type_definition = copy_type(base.return_type_definition, generic_types);
    else
        function.return_type = base.return_type;

    array_resize(&function.arguments, base.arguments.length, allocate, reallocate);
    each argument, i in base.arguments {
        if argument.flags & AstFlags.HasGenerics
            function.arguments[i] = copy_declaration(argument, generic_types, base.generics);
        else
            function.arguments[i] = argument;
    }

    function.body = copy_scope(base.body, generic_types, base.generics);

    return function;
}

OperatorOverloadAst* create_polymorphed_operator_overload(OperatorOverloadAst* base, Array<TypeAst*> generic_types) {
    overload := copy_ast(base);
    overload.op = base.op;
    overload.type = copy_type(base.type, generic_types);
    overload.name = format_string("operator % %", allocate, print_operator(overload.op), print_type_definition(overload.type));
    overload.function_index = get_function_index();
    overload.function_flags = base.function_flags;

    if overload.function_flags & FunctionFlags.ReturnTypeHasGenerics
        overload.return_type_definition = copy_type(base.return_type_definition, generic_types);
    else
        overload.return_type = base.return_type;

    array_resize(&overload.arguments, base.arguments.length, allocate, reallocate);
    each argument, i in base.arguments {
        overload.arguments[i] = copy_declaration(argument, generic_types, base.generics);
    }

    overload.body = copy_scope(base.body, generic_types, base.generics);

    return overload;
}

TypeDefinition* copy_type(TypeDefinition* type, Array<TypeAst*> generic_types) {
    copy_type := copy_ast(type);

    if type.flags & AstFlags.IsGeneric {
        generic_type := generic_types[type.generic_index];
        copy_type.name = generic_type.name;
        copy_type.baked_type = generic_type;
    }
    else {
        copy_type.name = type.name;
        copy_type.count = type.count;
        if type.flags & AstFlags.Compound copy_type.flags |= AstFlags.Compound;

        array_resize(&copy_type.generics, type.generics.length, allocate, reallocate);
        each generic, i in type.generics {
            copy_type.generics[i] = copy_type(generic, generic_types);
        }
    }

    return copy_type;
}

ScopeAst* copy_scope(ScopeAst* scope, Array<TypeAst*> generic_types, Array<string> generics) {
    copy := copy_ast(scope);
    array_reserve(&copy.children, scope.children.length, allocate, reallocate);

    each ast, i in scope.children {
        copy.children[i] = copy_ast(ast, generic_types, generics);
    }

    return copy;
}

Ast* copy_ast(Ast* ast, Array<TypeAst*> generic_types, Array<string> generics) {
    if ast == null return null;

    switch ast.ast_type {
        case AstType.Constant;
        case AstType.Null;
        case AstType.Break;
        case AstType.Continue;
            return ast;
        case AstType.Scope;
            return copy_scope(cast(ScopeAst*, ast), generic_types, generics);
        case AstType.StructFieldRef; {
            struct_field := cast(StructFieldRefAst*, ast);
            copy := copy_ast(struct_field);
            array_resize(&copy.children, struct_field.children.length, allocate, reallocate);
            each child, i in struct_field.children {
                if child.ast_type == AstType.Identifier copy.children[i] = child;
                else copy.children[i] = copy_ast(child, generic_types, generics);
            }
            return copy;
        }
        case AstType.Return; {
            return_ast := cast(ReturnAst*, ast);
            copy := copy_ast(return_ast);
            copy.value = copy_ast(return_ast.value, generic_types, generics);
            return copy;
        }
        case AstType.Identifier; {
            identifier := cast(IdentifierAst*, ast);
            each generic, i in generics {
                if generic == identifier.name {
                    generic_type := generic_types[i];
                    copy := copy_ast(identifier);
                    copy.name = generic_type.name;
                    copy.type_index = generic_type.type_index;
                    copy.baked_type = generic_type;
                    return copy;
                }
            }
            return ast;
        }
        case AstType.Expression; {
            expression := cast(ExpressionAst*, ast);
            copy := copy_ast(expression);
            copy.op = expression.op;
            copy.l_value = copy_ast(expression.l_value, generic_types, generics);
            copy.r_value = copy_ast(expression.r_value, generic_types, generics);
            return copy;
        }
        case AstType.CompoundExpression; {
            expression := cast(CompoundExpressionAst*, ast);
            copy := copy_ast(expression);
            array_resize(&copy.children, expression.children.length, allocate, reallocate);
            each subexpression, i in expression.children {
                copy.children[i] = copy_ast(subexpression, generic_types, generics);
            }
            return copy;
        }
        case AstType.ChangeByOne; {
            change_by_one := cast(ChangeByOneAst*, ast);
            copy := copy_ast(change_by_one);
            copy.flags = change_by_one.flags;
            copy.value = copy_ast(change_by_one.value, generic_types, generics);
            return copy;
        }
        case AstType.Unary; {
            unary := cast(UnaryAst*, ast);
            copy := copy_ast(unary);
            copy.op = unary.op;
            copy.value = copy_ast(unary.value, generic_types, generics);
            return copy;
        }
        case AstType.Call; {
            call := cast(CallAst*, ast);
            copy := copy_ast(call);
            if call.generics.length {
                array_resize(&copy.generics, call.generics.length, allocate, reallocate);
                each generic, i in call.generics {
                    copy.generics[i] = copy_type(generic, generic_types);
                }
            }
            if call.specified_arguments.initialized {
                copy.specified_arguments = call.specified_arguments;
            }
            if call.arguments.length {
                array_resize(&copy.arguments, call.arguments.length, allocate, reallocate);
                each argument, i in call.arguments {
                    copy.arguments[i] = copy_ast(argument, generic_types, generics);
                }
            }
            return copy;
        }
        case AstType.Declaration;
            return copy_declaration(cast(DeclarationAst*, ast), generic_types, generics);
        case AstType.CompoundDeclaration; {
            declaration := cast(CompoundDeclarationAst*, ast);
            copy := copy_ast(declaration);
            array_resize(&copy.variables, declaration.variables.length, allocate, reallocate);
            each variable, i in declaration.variables {
                var_copy := copy_ast(variable);
                var_copy.name = variable.name;
                copy.variables[i] = var_copy;
            }
            copy_declarations(declaration, copy, generic_types, generics);
            return copy;
        }
        case AstType.Assignment;
            return copy_assignment(cast(AssignmentAst*, ast), generic_types, generics);
        case AstType.Conditional; {
            conditional := cast(ConditionalAst*, ast);
            copy := copy_ast(conditional);
            copy.condition = copy_ast(conditional.condition, generic_types, generics);
            copy.if_block = copy_scope(conditional.if_block, generic_types, generics);
            if conditional.else_block
                copy.else_block = copy_scope(conditional.else_block, generic_types, generics);
            return copy;
        }
        case AstType.While; {
            while_ast := cast(WhileAst*, ast);
            copy := copy_ast(while_ast);
            copy.condition = copy_ast(while_ast.condition, generic_types, generics);
            copy.body = copy_scope(while_ast.body, generic_types, generics);
            return copy;
        }
        case AstType.Each; {
            each_ast := cast(EachAst*, ast);
            copy := copy_ast(each_ast);
            copy.iteration_variable = each_ast.iteration_variable;
            copy.index_variable = each_ast.index_variable;
            copy.iteration = copy_ast(each_ast.iteration, generic_types, generics);
            copy.range_begin = copy_ast(each_ast.range_begin, generic_types, generics);
            copy.range_end = copy_ast(each_ast.range_end, generic_types, generics);
            copy.body = copy_scope(each_ast.body, generic_types, generics);
            return copy;
        }
        case AstType.Index; {
            index := cast(IndexAst*, ast);
            copy := copy_ast(index);
            copy.name = index.name;
            copy.index = copy_ast(index.index, generic_types, generics);
            return copy;
        }
        case AstType.CompilerDirective; {
            directive := cast(CompilerDirectiveAst*, ast);
            copy := copy_ast(directive);
            copy.directive_type = directive.directive_type;
            copy.value = copy_ast(directive.value, generic_types, generics);
            return copy;
        }
        case AstType.Cast; {
            cast_ast := cast(CastAst*, ast);
            copy := copy_ast(cast_ast);
            if cast_ast.flags & AstFlags.HasGenerics copy.target_type_definition = copy_type(cast_ast.target_type_definition, generic_types);
            else copy.target_type_definition = cast_ast.target_type_definition;
            copy.value = copy_ast(cast_ast.value, generic_types, generics);
            return copy;
        }
        case AstType.Assembly; {
            assembly := cast(AssemblyAst*, ast);
            if assembly.in_registers.length == 0 || assembly.out_values.length == 0 return assembly;

            copy := copy_ast(assembly);
            copy.instructions = assembly.instructions;
            table_init(&copy.in_registers, assembly.in_registers.length, 1.0);
            array_resize(&copy.out_values, assembly.out_values.length, allocate, reallocate);

            each entry in assembly.in_registers.entries {
                if entry.filled {
                    in_copy := copy_ast(entry.value);
                    in_copy.register = entry.key;
                    in_copy.ast = copy_ast(entry.value.ast, generic_types, generics);
                    table_add(&copy.in_registers, entry.key, in_copy);
                }
            }

            each out_value, i in assembly.out_values {
                value_copy := copy_ast(out_value);
                value_copy.register = out_value.register;
                value_copy.ast = copy_ast(out_value.ast, generic_types, generics);
                copy.out_values[i] = value_copy;
            }
            return copy;
        }
        case AstType.TypeDefinition;
            return copy_type(cast(TypeDefinition*, ast), generic_types);
        case AstType.Defer; {
            defer_ast := cast(DeferAst*, ast);
            copy := copy_ast(defer_ast);
            copy.statement = copy_scope(defer_ast.statement, generic_types, generics);
            return copy;
        }
    }

    assert(false);
    return null;
}

DeclarationAst* copy_declaration(DeclarationAst* ast, Array<TypeAst*> generic_types, Array<string> generics) {
    copy := copy_ast(ast);
    copy.name = ast.name;
    if ast.flags & AstFlags.Constant copy.flags = AstFlags.Constant;
    copy_declarations(ast, copy, generic_types, generics);

    return copy;
}

copy_declarations(Declaration* source, Declaration* target, Array<TypeAst*> generic_types, Array<string> generics) {
    if source.flags & AstFlags.HasGenerics target.type_definition = copy_type(source.type_definition, generic_types);
    else target.type_definition = source.type_definition;

    target.value = copy_ast(source.value, generic_types, generics);

    if source.assignments {
        target.assignments = source.assignments;
    }
    else if source.array_values.length {
        array_resize(&target.array_values, source.array_values.length, allocate, reallocate);
        each value, i in source.array_values {
            target.array_values[i] = copy_ast(value, generic_types, generics);
        }
    }
}

AssignmentAst* copy_assignment(AssignmentAst* ast, Array<TypeAst*> generic_types, Array<string> generics) {
    copy := copy_ast(ast);
    copy.reference = copy_ast(ast.reference, generic_types, generics);
    copy.op = ast.op;
    copy.value = copy_ast(ast.value, generic_types, generics);
    return copy;
}

T* copy_ast<T>(T* source) {
    ast := new<T>();

    ast.ast_type = source.ast_type;
    ast.file_index = source.file_index;
    ast.line = source.line;
    ast.column = source.column;

    return ast;
}
