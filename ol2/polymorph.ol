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
        case AstType.StructFieldRef; {}
        case AstType.Return; {}
        case AstType.Identifier; {}
        case AstType.Expression; {}
        case AstType.CompoundExpression; {}
        case AstType.ChangeByOne; {}
        case AstType.Unary; {}
        case AstType.Call; {}
        case AstType.Declaration; {}
        case AstType.CompoundDeclaration; {}
        case AstType.Assignment; {}
        case AstType.Conditional; {}
        case AstType.While; {}
        case AstType.Each; {}
        case AstType.Index; {}
        case AstType.CompilerDirective; {}
        case AstType.Cast; {}
        case AstType.Assembly; {}
        case AstType.TypeDefinition;
            return copy_type(cast(TypeDefinition*, ast), generic_types);
        case AstType.Defer; {}
    }

    assert(false);
    return null;
}

DeclarationAst* copy_declaration(Ast* ast, Array<TypeAst*> generic_types, Array<string> generics) {
    return null;
}

T* copy_ast<T>(T* source) {
    ast := new<T>();

    ast.ast_type = source.ast_type;
    ast.file_index = source.file_index;
    ast.line = source.line;
    ast.column = source.column;

    return ast;
}
