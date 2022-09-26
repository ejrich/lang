#import math
#import "ir_builder.ol"
#import "polymorph.ol"
#import "runner.ol"
#import "type_table.ol"

init_types() {
    init_global_scope(&global_scope, 50);

    add_primitive(&void_type);
    add_primitive(&bool_type);
    add_primitive(&s8_type);
    add_primitive(&u8_type);
    add_primitive(&s16_type);
    add_primitive(&u16_type);
    add_primitive(&s32_type);
    add_primitive(&u32_type);
    add_primitive(&s64_type);
    add_primitive(&u64_type);
    add_primitive(&float_type);
    add_primitive(&float64_type);
    add_primitive(&type_type);
}

init_global_scope(GlobalScope* scope, int capacity = 10) {
    table_init(&scope.identifiers, capacity);
    table_init(&scope.functions, capacity);
    table_init(&scope.polymorphic_structs, capacity);
    table_init(&scope.polymorphic_functions, capacity);
}

add_primitive(TypeAst* type) {
    table_add(&global_scope.identifiers, type.name, type);

    add_to_type_table(type);
    create_type_info(type);
}

init_necessary_types() {
    verify_struct(string_type);
    verify_struct(any_type);

    raw_string_type = get_global_type("u8*");
    type_info_pointer_type = get_global_type("TypeInfo*");
    void_pointer_type = get_global_type("void*");
}

TypeAst* get_global_type(string name) {
    found, type := table_get(global_scope.identifiers, name);
    assert(found);
    assert((type.flags & AstFlags.IsType) == AstFlags.IsType);

    return cast(TypeAst*, type);
}

verify_compiler_directives() {
    // Verify compiler directives, collecting run directives to be handled afterwards
    while directives.head {
        node := directives.head;
        parsing_additional: bool;

        while node {
            directive := node.data;
            remove_node(&directives, node, null);

            switch directive.directive_type {
                case DirectiveType.Run; {} // TODO Figure out how to do this
                case DirectiveType.If; {
                    conditional := cast(ConditionalAst*, directive.value);
                    constant: bool;
                    if verify_condition(conditional.condition, null, &constant, true) {
                        if !constant complete_work();

                        condition := create_runnable_condition(directive.value);
                        if execute_condition(condition, directive.value) {
                            each ast in conditional.if_block.children {
                                add_additional_ast(ast, &parsing_additional);
                            }
                        }
                        else if conditional.else_block {
                            each ast in conditional.else_block.children {
                                add_additional_ast(ast, &parsing_additional);
                            }
                        }
                    }
                }
                case DirectiveType.Assert; {
                    constant: bool;
                    if verify_condition(directive.value, null, &constant, true) {
                        if !constant complete_work();

                        condition := create_runnable_condition(directive.value);
                        if !execute_condition(condition, directive.value)
                            report_error("Assertion failed", directive.value);
                    }
                }
                default; assert(false);
            }

            node = node.next;
        }

        if parsing_additional complete_work();
    }



    // Evaluate run directives on separate thread to handle metaprogramming
}

check_types() {
    // Process the ast queue, emitting a message on success/failure

    // If the metaprogram takes control, let the message queue flow until it has been cleared
    // - Wait for sent messages to be processed before moving to next flow
    // - Once fully typechecked, generate ir
    // - Send message that code is ready to be generated, wait for processing
    // - Send message that exe is ready to be linked, wait for processing
    // - Send message that exe has been linked, wait for processing and send null
}

clear_ast_queue() {
    // TODO Implement me
}

add_additional_ast(Ast* ast, bool* parsing_additional) {
    switch ast.ast_type {
        case AstType.Function;
            add_function(cast(FunctionAst*, ast));
        case AstType.OperatorOverload;
            add_overload(cast(OperatorOverloadAst*, ast));
        case AstType.Enum; {
            enum_ast := cast(EnumAst*, ast);
            if add_type(enum_ast) create_type_info(enum_ast);
        }
        case AstType.Struct; {
            struct_ast := cast(StructAst*, ast);
            if struct_ast.generics.length {
                add_polymorphic_struct(struct_ast);
                return;
            }
            add_type(struct_ast);
        }
        case AstType.Union;
        case AstType.Interface;
            add_type(cast(TypeAst*, ast));
        case AstType.Declaration;
            add_global_variable(cast(DeclarationAst*, ast));
        case AstType.CompilerDirective; {
            directive := cast(CompilerDirectiveAst*, ast);
            switch directive.directive_type {
                case DirectiveType.ImportModule;
                    if add_module(directive) { *parsing_additional = true; }
                case DirectiveType.ImportFile;
                    if add_file(directive) { *parsing_additional = true; }
                case DirectiveType.Library;
                    add_library(directive);
                case DirectiveType.SystemLibrary;
                    add_system_library(directive);
                default;
                    add(&directives, directive);
            }
            return;
        }
    }

    add(&asts, ast);
}

bool add_global_variable(DeclarationAst* declaration) {
    return add_identifier(declaration.name, declaration, declaration.file_index);
}

add_function(FunctionAst* function) {
    if function.generics.length {
        if add_overload_if_not_exists_for_polymorphic_function(function) {
            report_error("Polymorphic function '%' has multiple overloads with arguments (%)", function, function.name, print_argument_types(function.arguments));
        }
    }
    else {
        if function.flags & AstFlags.Extern {
            if function.flags & AstFlags.Private {
                report_error("Extern function '%' must be public to avoid linking failures", function, function.name);
            }
            else {
                found, _ := get_existing_function(function.name, function.file_index);
                if found report_error("Multiple definitions of extern function '%'", function, function.name);
            }
        }
        else if overload_exists_for_function(function) {
            report_error("Function '%' has multiple overloads with arguments (%)", function, function.name, print_argument_types(function.arguments));
        }

        add_function(function.name, function.file_index, function);
    }
}

bool overload_exists_for_function(FunctionAst* function) {
    name := function.name;
    if string_is_empty(name) return false;

    private_scope := private_scopes[function.file_index];

    if private_scope {
        found, functions := table_get(private_scope.functions, name);

        if found && overload_exists(function, *functions)
            return true;

        found, functions = table_get(global_scope.functions, name);
        if found return overload_exists(function, *functions);
    }
    else {
        found, functions := table_get(global_scope.functions, name);

        if found return overload_exists(function, *functions);
    }

    return false;
}

bool add_overload_if_not_exists_for_polymorphic_function(FunctionAst* function) {
    name := function.name;
    if string_is_empty(name) return false;

    private_scope := private_scopes[function.file_index];

    if private_scope {
        found, functions := table_get(private_scope.polymorphic_functions, name);
        if found && overload_exists(function, *functions) return true;

        found_global, global_functions := table_get(global_scope.polymorphic_functions, name);
        if found_global && overload_exists(function, *global_functions) return true;

        if !found {
            functions = new<Array<FunctionAst*>>();
            table_add(&private_scope.polymorphic_functions, name, functions);
        }

        array_insert(functions, function, allocate, reallocate);
    }
    else {
        found, functions := table_get(global_scope.polymorphic_functions, name);

        if found {
            if overload_exists(function, *functions) return true;
        }
        else {
            functions = new<Array<FunctionAst*>>();
            table_add(&global_scope.polymorphic_functions, name, functions);
        }

        array_insert(functions, function, allocate, reallocate);
    }

    return false;
}

bool overload_exists(FunctionAst* function, Array<FunctionAst*> functions) {
    each func in functions {
        if func.arguments.length == function.arguments.length {
            match := true;

            each argument, i in function.arguments {
                if !type_definition_equals(argument.type_definition, func.arguments[i].type_definition) {
                    match = false;
                    break;
                }
            }

            if match return true;
        }
    }

    return false;
}

bool type_definition_equals(TypeDefinition* a, TypeDefinition* b) {
    if a == null || b == null return false;
    if a.name != b.name || a.generics.length != b.generics.length return false;

    each generic, i in a.generics {
        if !type_definition_equals(generic, b.generics[i]) return false;
    }

    return true;
}

string print_argument_types(Array<DeclarationAst*> arguments) #inline {
    if arguments.length == 0 return "";

    length := -2;
    each argument in arguments {
        length += determine_type_definition_length(argument.type_definition) + 2; // Add 2 for the ", " after each argument
    }

    result_data: Array<u8>[length];
    result: string = { length = length; data = result_data.data; }

    i, index := 0;
    while i < arguments.length - 1 {
        index = add_type_to_string(arguments[i++].type_definition, result, index);
        result[index++] = ',';
        result[index++] = ' ';
    }
    add_type_to_string(arguments[i].type_definition, result, index);

    return result;
}

add_function(string name, int file_index, FunctionAst* function) {
    if string_is_null(name) return;

    function.function_index = get_function_index();

    if function.flags & AstFlags.Private {
        private_scope := private_scopes[file_index];

        add_function_to_scope(name, function, private_scope);
    }
    else add_function_to_scope(name, function, &global_scope);
}

add_function_to_scope(string name, FunctionAst* function, GlobalScope* scope) {
    found, functions := table_get(scope.functions, name);
    if !found {
        functions = new<Array<FunctionAst*>>();
        table_add(&scope.functions, name, functions);
    }

    array_insert(functions, function, allocate, reallocate);
}

add_overload(OperatorOverloadAst* overload) {
}

bool add_polymorphic_struct(StructAst* struct_ast) {
    name := struct_ast.name;
    error_format := "Multiple definitions of polymorphic struct '%'"; #const

    if struct_ast.flags & AstFlags.Private {
        private_scope := private_scopes[struct_ast.file_index];

        if table_contains(private_scope.polymorphic_structs, name) || table_contains(global_scope.polymorphic_structs, name) {
            report_error(error_format, struct_ast, name);
            return false;
        }

        table_add(&private_scope.polymorphic_structs, name, struct_ast);
    }
    else if !table_add(&global_scope.polymorphic_structs, name, struct_ast) {
        report_error(error_format, struct_ast, name);
        return false;
    }

    return true;
}

add_library(CompilerDirectiveAst* directive) {
    library := directive.library;

    #if os == OS.Linux {
        archive := concat_temp(library.absolute_path, ".a");
        shared_library := concat_temp(library.absolute_path, ".so");

        if !file_exists(archive) && !file_exists(shared_library)
            report_error("Unable to find .a/.so '%' of library '%'", directive, library.path, library.name);
    }
    #if os == OS.Windows {
        lib := concat_temp(library.absolute_path, ".lib");
        dll := concat_temp(library.absolute_path, ".dll");

        if !file_exists(lib) && !file_exists(dll)
            report_error("Unable to find .lib/.dll '%' of library '%'", directive, library.path, library.name);
    }

    if !table_add(&libraries, library.name, &directive.library)
        report_error("Library '%' already defined", directive, library.name);
}

add_system_library(CompilerDirectiveAst* directive) {
    library := directive.library;

    if !string_is_empty(library.lib_path) && !file_exists(library.lib_path)
        report_error("Library path '%' of library '%' does not exist", directive, library.lib_path, library.name);

    if !table_add(&libraries, library.name, &directive.library)
        report_error("Library '%' already defined", directive, library.name);
}

bool add_type(TypeAst* type) {
    return add_type(type.name, type, type.file_index);
}

bool add_type(string name, TypeAst* type, int file_index) {
    added := add_identifier(name, type, type.file_index);
    if added add_to_type_table(type);

    return added;
}

bool add_identifier(string name, Ast* ast, int file_index) {
    error_format :=  "Identifier '%' already defined"; #const

    if ast.flags & AstFlags.Private {
        private_scope := private_scopes[file_index];

        if table_contains(private_scope.identifiers, name) || table_contains(global_scope.identifiers, name) {
            report_error(error_format, ast, name);
            return false;
        }

        table_add(&private_scope.identifiers, name, ast);
    }
    else if !table_add(&global_scope.identifiers, name, ast) {
        report_error(error_format, ast, name);
        return false;
    }

    return true;
}

TypeAst* get_type(string name, int file_index) {
    private_scope := private_scopes[file_index];

    if private_scope == null {
        found, ast := table_get(global_scope.identifiers, name);
        if found return is_type(ast);

        return null;
    }

    found, ast := table_get(private_scope.identifiers, name);
    if found return is_type(ast);

    found, ast = table_get(global_scope.identifiers, name);
    if found return is_type(ast);

    return null;
}

TypeAst* is_type(Ast* ast) {
    if ast.flags & AstFlags.IsType return cast(TypeAst*, ast);
    return null;
}

StructAst* get_polymorphic_struct(string name, int file_index) {
    private_scope := private_scopes[file_index];

    if private_scope {
        found, type := table_get(private_scope.polymorphic_structs, name);
        if found return type;
    }

    _, type := table_get(global_scope.polymorphic_structs, name);
    return type;
}

verify_struct(StructAst* struct_ast) {
    struct_ast.flags |= AstFlags.Verifying;
    field_names: HashSet<string>;
    i := 0;

    if struct_ast.base_type_definition {
        not_struct: bool;
        base_type := verify_type(struct_ast.base_type_definition, &not_struct, &not_struct, &not_struct);

        if not_struct {
            report_error("Struct base type must be a struct", struct_ast.base_type_definition);
        }
        else if base_type == null {
            report_error("Undefined type '%' as the base type of struct '%'", struct_ast.base_type_definition, print_type_definition(struct_ast.base_type_definition), struct_ast.name);
        }
        else if base_type.ast_type != AstType.Struct {
            report_error("Base type '%' of struct '%' is not a struct", struct_ast.base_type_definition, print_type_definition(struct_ast.base_type_definition), struct_ast.name);
        }
        else {
            base_struct := cast(StructAst*, base_type);
            if struct_ast == base_struct {
                report_error("Base struct cannot be the same as the struct", struct_ast.base_type_definition);
            }
            else {
                array_insert_range(&struct_ast.fields, 0, base_struct.fields, allocate, reallocate);
                field_names = create_temp_set<string>(struct_ast.fields.length);

                if base_struct.flags & AstFlags.Verified {
                    each field in base_struct.fields {
                        set_add(&field_names, field.name);
                    }

                    struct_ast.size = base_struct.size;
                    struct_ast.alignment = base_struct.alignment;
                }
                else {
                    each field in base_struct.fields {
                        set_add(&field_names, field.name);

                        if field.type == null {
                            field.type = verify_type(field.type_definition);
                        }

                        if field.type {
                            if field.type.alignment > struct_ast.alignment
                                struct_ast.alignment = field.type.alignment;

                            alignment_offset := struct_ast.size % field.type.alignment;
                            if alignment_offset struct_ast.size += field.type.alignment - alignment_offset;

                            field.offset = struct_ast.size;
                            struct_ast.size += field.type.size;
                        }
                    }
                }

                struct_ast.base_struct = base_struct;
                i = base_struct.fields.length;
            }
        }
    }

    if !field_names.initialized field_names = create_temp_set<string>(struct_ast.fields.length);

    while i < struct_ast.fields.length {
        field := struct_ast.fields[i++];

        if !set_add(&field_names, field.name) {
            report_error("Struct '%' already contains field '%'", struct_ast, field.name);
        }

        if field.type_definition {
            is_generic, is_varargs, is_params: bool;
            field.type = verify_type(field.type_definition, &is_generic, &is_varargs, &is_params);

            if (is_varargs || is_params)
            {
                report_error("Struct field '%.%' cannot be varargs or Params", field.type_definition, struct_ast.name, field.name);
            }
            else if field.type == null {
                report_error("Undefined type '%' in struct field '%.%'", field.type_definition, print_type_definition(field.type_definition), struct_ast.name, field.name);
            }
            else if field.type.type_kind == TypeKind.Void {
                report_error("Struct field '%.%' cannot be assigned type 'void'", field.type_definition, struct_ast.name, field.name);
            }
            else if (field.type.type_kind == TypeKind.Array) {
                array_struct := cast(StructAst*, field.type);
                field.array_element_type = array_struct.generic_types[0];
            }
            else if (field.type.type_kind == TypeKind.CArray) {
                array_type := cast(ArrayType*, field.type);
                field.array_element_type = array_type.element_type;
            }

            if field.value {
                if field.value.ast_type == AstType.Null {
                    if field.type != null && field.type.type_kind != TypeKind.Pointer
                        report_error("Cannot assign null to non-pointer type", field.value);
                    else {
                        null_ast := cast(NullAst*, field.value);
                        null_ast.target_type = field.type;
                    }
                }
                else {
                    is_constant: bool;
                    value_type := verify_expression(field.value, null, &is_constant);

                    // Verify the type is correct
                    if value_type {
                        if !type_equals(field.type, value_type)
                            report_error("Expected struct field value to be type '%', but got '%'", field.value, print_type_definition(field.type_definition), value_type.name);
                        else if !is_constant
                            report_error("Default values in structs must be constant", field.value);
                        else
                            verify_constant_if_necessary(field.value, field.type);
                    }
                }
            }
            else if field.assignments {
                if field.type != null && field.type.type_kind != TypeKind.Struct && field.type.type_kind != TypeKind.String {
                    report_error("Can only use object initializer with struct type, got '%'", field.type_definition, print_type_definition(field.type_definition));
                }
                // Catch circular references
                else if field.type != null && field.type != struct_ast {
                    struct_def := cast(StructAst*, field.type);
                    // foreach (var (name, assignment) in field.Assignments)
                    // {
                    //     VerifyFieldAssignment(structDef, name, assignment, null, GlobalScope, field: true);
                    // }
                }
            }
            else if field.array_values.length {
                if field.type != null && field.type.type_kind != TypeKind.Array && field.type.type_kind != TypeKind.CArray {
                    report_error("Cannot use array initializer to declare non-array type '%'", field.type_definition, print_type_definition(field.type_definition));
                }
                else {
                    field.type_definition.const_count = field.array_values.length;
                    element_type := field.array_element_type;
                    each value in field.array_values {
                        is_constant: bool;
                        value_type := verify_expression(value, null, &is_constant);
                        if value_type {
                            if !type_equals(element_type, value_type)
                                report_error("Expected array value to be type '%', but got '%'", value, element_type.name, value_type.name);
                            else if !is_constant
                                report_error("Default values in structs array initializers should be constant", value);
                            else
                                verify_constant_if_necessary(value, element_type);
                        }
                    }
                }
            }

            // Check type count
            if field.type != null && field.type.type_kind == TypeKind.Array && field.type_definition.count != null {
                // Verify the count is a constant
                is_constant: bool;
                array_length: s32;
                count_type := verify_expression(field.type_definition.count, null, &is_constant, &array_length);

                if count_type == null || count_type.type_kind != TypeKind.Integer || !is_constant || array_length < 0
                    report_error("Expected size of '%.%' to be a constant, positive integer", field.type_definition.count, struct_ast.name, field.name);
                else
                    field.type_definition.const_count = array_length;
            }
        }
        else {
            if field.value {
                if (field.value.ast_type == AstType.Null) {
                    report_error("Cannot assign null value without declaring a type", field.value);
                }
                else {
                    is_constant: bool;
                    value_type := verify_expression(field.value, null, &is_constant);

                    if !is_constant {
                        report_error("Default values in structs must be constant", field.value);
                    }
                    else if value_type != null && value_type.type_kind == TypeKind.Void {
                        report_error("Struct field '%.%' cannot be assigned type 'void'", field.value, struct_ast.name, field.name);
                    }
                    field.type = value_type;
                }
            }
            else if field.assignments
                report_error("Struct literals are not yet supported", field);
            else if field.array_values.length
                report_error("Declaration for struct field '%.%' with array initializer must have the type declared", field, struct_ast.name, field.name);
        }

        // Check for circular dependencies and set the size and offset
        if struct_ast == field.type {
            report_error("Struct '{struct_ast.Name}' contains circular reference in field '{field.Name}'", field);
        }
        else if field.type {
            if field.type.alignment > struct_ast.alignment
                struct_ast.alignment = field.type.alignment;

            alignment_offset := struct_ast.size % field.type.alignment;
            if alignment_offset struct_ast.size += field.type.alignment - alignment_offset;

            field.offset = struct_ast.size;
            struct_ast.size += field.type.size;
        }
    }

    if struct_ast.size {
        alignment_offset := struct_ast.size % struct_ast.alignment;
        if alignment_offset struct_ast.size += struct_ast.alignment - alignment_offset;
    }

    create_type_info(struct_ast);
    struct_ast.flags |= AstFlags.Verified;
}

verify_union(UnionAst* union_ast) {
    union_ast.flags |= AstFlags.Verifying;

    if union_ast.fields.length {
        field_names := create_temp_set<string>(union_ast.fields.length);

        each field in union_ast.fields {
            if !set_add(&field_names, field.name) {
                report_error("Union '%' already contains field '%'", union_ast, field.name);
            }

            is_generic, is_varargs, is_params: bool;
            field.type = verify_type(field.type_definition, &is_generic, &is_varargs, &is_params);

            if is_varargs || is_params {
                report_error("Union field '%.%' cannot have varargs or params", field.type_definition, union_ast.name, field.name);
            }
            else if field.type == null {
                report_error("Undefined type '%' union field '%.%' cannot have varargs or params", field.type_definition, print_type_definition(field.type_definition), union_ast.name, field.name);
            }
            else if field.type.type_kind == TypeKind.Void {
                report_error("Union field '%.%' cannot be assigned type 'void'", field.type_definition, union_ast.name, field.name);
            }
            else {
                if field.type.type_kind == TypeKind.Array && field.type_definition.count != null {
                    is_constant: bool;
                    array_length: s32;
                    count_type := verify_expression(field.type_definition.count, null, &is_constant, &array_length);

                    if count_type != null && count_type.type_kind != TypeKind.Integer || is_constant || array_length < 0
                        report_error("Expected size of '%.%' to be a constant, positive integer", field.type_definition.count, union_ast.name, field.name);
                    else field.type_definition.const_count = array_length;
                }

                if union_ast == field.type
                    report_error("Union '%' contains circular reference in field '%'", field, union_ast.name, field.name);
                else {
                    if field.type.alignment > union_ast.alignment union_ast.alignment = field.type.alignment;
                    if field.type.size > union_ast.size           union_ast.size = field.type.size;
                }
            }
        }
    }
    else report_error("Union '%' must have 1 or more fields", union_ast, union_ast.name);

    if union_ast.size {
        alignment_offset := union_ast.size % union_ast.alignment;
        if alignment_offset union_ast.size += union_ast.alignment - alignment_offset;
    }

    create_type_info(union_ast);
    union_ast.flags |= AstFlags.Verified;
}

verify_interface(InterfaceAst* interface_ast) {
    interface_ast.flags |= AstFlags.Verifying;

    if interface_ast.return_type_definition {
        _, is_varargs, is_params: bool;
        interface_ast.return_type = verify_type(interface_ast.return_type_definition, &_, &is_varargs, &is_params);

        if is_varargs || is_params {
            report_error("Return type of interface '%' cannot be varargs or Params", interface_ast.return_type_definition, interface_ast.name);
        }
        else if interface_ast.return_type == null {
            report_error("Return type '%' of interface '%' is not defined", interface_ast.return_type_definition, print_type_definition(interface_ast.return_type_definition), interface_ast.name);
        }
        else if interface_ast.return_type.type_kind == TypeKind.Array && interface_ast.return_type_definition.count != null {
            report_error("Size of Array does not need to be specified for return type of interface '%'", interface_ast.return_type_definition.count, interface_ast.name);
        }
    }
    else interface_ast.return_type = &void_type;

    if interface_ast.arguments.length {
        interface_ast.argument_count = interface_ast.arguments.length;
        arg_names := create_temp_set<string>(interface_ast.arguments.length);

        each argument in interface_ast.arguments {
            if !set_add(&arg_names, argument.name) {
                report_error("Interface '%' already contains argument '%'", argument, interface_ast.name, argument.name);
            }

            _, is_varargs, is_params: bool;
            argument.type = verify_type(argument.type_definition, &_, &is_varargs, &is_params);

            if is_varargs || is_params {
                report_error("Interface '%' cannot be varargs or Params arguments", argument, interface_ast.name);
            }
            else {
                report_error("Type '%' of argument '%' in interface '%' is not defined", argument.type_definition, print_type_definition(argument.type_definition), argument.name, interface_ast.name);
            }
        }
    }

    create_type_info(interface_ast);
    interface_ast.flags |= AstFlags.Verified;
}

bool verify_condition(Ast* ast, Function* function, bool* constant, bool will_run = false) {
    type := verify_expression(ast, function, constant);

    if will_run && !*constant clear_ast_queue();

    if type {
        switch type.type_kind {
            case TypeKind.Boolean;
            case TypeKind.Integer;
            case TypeKind.Float;
            case TypeKind.Pointer;
            case TypeKind.Enum;
                return errors.length == 0;
        }

        report_error("Expected condition to be bool, int, float, or pointer, but got '%'", ast, type.name);
    }

    return false;
}

TypeAst* get_reference(Ast* ast, Function* function, Scope* scope, bool* has_pointer, bool from_unary_reference = false) {
    // TODO Implement me
    return null;
}

TypeAst* verify_expression(Ast* ast, Function* function, Scope* scope) {
    _: bool;
    return verify_expression(ast, function, scope, &_, &_);
}

TypeAst* verify_expression(Ast* ast, Function* function, bool* is_constant) {
    _: bool;
    scope := private_scopes[ast.file_index];
    if scope == null scope = &global_scope;
    return verify_expression(ast, function, scope, is_constant, &_);
}

TypeAst* verify_expression(Ast* ast, Function* function, Scope* scope, bool* is_constant) {
    _: bool;
    return verify_expression(ast, function, scope, is_constant, &_);
}

TypeAst* verify_expression(Ast* ast, Function* function, bool* is_constant, s32* array_length) {
    _: bool;
    scope := private_scopes[ast.file_index];
    if scope == null scope = &global_scope;
    return verify_expression(ast, function, scope, is_constant, &_, true, array_length);
}

TypeAst* verify_expression(Ast* ast, Function* function, Scope* scope, bool* is_constant, s32* array_length) {
    _: bool;
    return verify_expression(ast, function, scope, is_constant, &_, true, array_length);
}

TypeAst* verify_expression(Ast* ast, Function* function, Scope* scope, bool* is_constant, bool* is_type, bool get_array_length = false, s32* array_length = null) {
    // TODO Implement me
    if ast == null return null;

    switch ast.ast_type {
        case AstType.StructFieldRef; {
        }
        case AstType.Constant; {
            *is_constant = true;
            constant := cast(ConstantAst*, ast);
            if get_array_length && constant.type != null && constant.type.type_kind == TypeKind.Integer {
                *array_length = constant.value.unsigned_integer;
            }
            else if constant.type == null && constant.string.data != null
                constant.type = string_type;

            return constant.type;
        }
        case AstType.Null; {
            *is_constant = true;
            return null;
        }
        case AstType.Identifier; {
        }
        case AstType.Expression; {
        }
        case AstType.CompoundExpression; {
            compound_expression := cast(CompoundExpressionAst*, ast);
            types: Array<TypeAst*>[compound_expression.children.length];
            type_names: Array<string>[types.length];

            error: bool;
            flags := AstFlags.None;
            size: u32;
            name_length := -2;
            each expr, i in compound_expression.children {
                type := verify_expression(expr, function, scope);
                if type == null
                    error = true;
                else {
                    size += type.size;
                    types[i] = type;
                    type_names[i] = type.name;
                    name_length += type.name.length + 2;
                    if type.flags & AstFlags.Private
                        flags = AstFlags.Private;
                }
            }

            if error return null;

            name_data: Array<u8>[name_length];
            name: string = { length = name_length; data = name_data.data; }

            i, index := 0;
            while i < types.length - 1 {
                type_name := type_names[i];
                memory_copy(name.data + index, type_name.data, type_name.length);
                index += type_name.length;
                name[index++] = ',';
                name[index++] = ' ';
            }
            type_name := type_names[i];
            memory_copy(name.data + index, type_name.data, type_name.length);

            compound_type := get_type(name, compound_expression.file_index);
            if compound_type return compound_type;

            return create_compound_type(types, name, size, flags, compound_expression.file_index);
        }
        case AstType.ChangeByOne; {
            change_by_one := cast(ChangeByOneAst*, ast);
            switch change_by_one.value.ast_type {
                case AstType.Identifier;
                case AstType.StructFieldRef;
                case AstType.Index; {
                    _: bool;
                    value_type := get_reference(change_by_one.value, function, scope, &_);
                    if value_type == null return null;

                    switch value_type.type_kind {
                        case TypeKind.Integer;
                        case TypeKind.Float; {
                            change_by_one.type = value_type;
                            return value_type;
                        }
                    }

                    if change_by_one.flags & AstFlags.Positive
                        report_error("Expected to increment int or float, but got type '%'", change_by_one, value_type.name);
                    else
                        report_error("Expected to decrement int or float, but got type '%'", change_by_one, value_type.name);
                    return null;
                }
            }

            if change_by_one.flags & AstFlags.Positive
                report_error("Expected to increment variable", change_by_one);
            else
                report_error("Expected to decrement variable", change_by_one);
            return null;
        }
        case AstType.Unary; {
            unary := cast(UnaryAst*, ast);

            if unary.op == UnaryOperator.Reference {
                has_pointer: bool;
                reference_type := get_reference(unary.value, function, scope, &has_pointer, true);

                if reference_type == null return null;
                if !has_pointer {
                    report_error("Unable to get reference of unary value", unary.value);
                    return null;
                }

                pointer_type: TypeAst*;
                if reference_type.type_kind == TypeKind.CArray {
                    array_type := cast(ArrayType*, reference_type);
                    name := concat_temp(array_type.element_type.name, "*");
                    pointer_type = get_type(name, unary.file_index);
                    if pointer_type == null
                        pointer_type = create_pointer_type(name, array_type.element_type);
                    unary.type = pointer_type;
                }
                else {
                    name := concat_temp(reference_type.name, "*");
                    pointer_type = get_type(name, unary.file_index);
                    if pointer_type == null
                        pointer_type = create_pointer_type(name, reference_type);
                    unary.type = pointer_type;
                }

                return pointer_type;
            }

            value_type := verify_expression(unary.value, function, scope);
            if value_type == null return null;

            switch unary.op {
                case UnaryOperator.Dereference; {
                    if value_type.type_kind == TypeKind.Pointer {
                        pointer_type := cast(PointerType*, value_type);
                        unary.type = pointer_type.pointed_type;
                        return pointer_type.pointed_type;
                    }

                    report_error("Cannot dereference type '%'", unary.value, value_type.name);
                    return null;
                }
                case UnaryOperator.Negate; {
                    switch value_type.type_kind {
                        case TypeKind.Integer;
                        case TypeKind.Float;
                            return value_type;
                    }

                    report_error("Unable to negate type '%'", unary.value, value_type.name);
                    return null;
                }
                case UnaryOperator.Not; {
                    if value_type.type_kind == TypeKind.Boolean return value_type;

                    report_error("Expected type 'bool', but got type '%'", unary.value, value_type.name);
                    return null;
                }
            }
        }
        case AstType.Call; {
        }
        case AstType.Index; {
        }
        case AstType.Cast; {
            cast_ast := cast(CastAst*, ast);
            target_type := verify_type(cast_ast.target_type_definition);
            value_type := verify_expression(cast_ast.value, function, scope);

            if target_type == null {
                report_error("Unable to cast to invalid type '%'", cast_ast, print_type_definition(cast_ast.target_type_definition));
                return null;
            }
            else if value_type == null
                return target_type;

            switch target_type.type_kind {
                case TypeKind.Integer;
                case TypeKind.Float;
                case TypeKind.Pointer;
                case TypeKind.Enum; {}
                default;
                    if value_type report_error("Unable to cast type '%' to '%'", cast_ast, value_type.name, target_type.name);
            }

            cast_ast.target_type = target_type;
            return target_type;
        }
        case AstType.TypeDefinition; {
            type_def := cast(TypeDefinition*, ast);
            type := verify_type(type_def);

            if type == null return null;
            *is_constant = true;
            *is_type = true;
            return type;
        }
        default; report_error("Invalid expression", ast);
    }

    return null;
}

TypeAst* verify_type(TypeDefinition* type, int depth = 0) {
    _: bool;
    return verify_type(type, &_, &_, &_, depth);
}

TypeAst* verify_type(TypeDefinition* type, bool* is_generic, int depth = 0) {
    _: bool;
    return verify_type(type, is_generic, &_, &_, depth);
}

TypeAst* verify_type(TypeDefinition* type, bool* is_generic, bool* is_varargs, bool* is_params, int depth = 0, bool allow_params = false, int* initial_array_length = null) {
    if type == null return null;
    if type.baked_type return type.baked_type;

    if type.flags & AstFlags.Generic {
        if type.generics.length
            report_error("Generic type cannot have additional generic types", type);

        *is_generic = true;
        return null;
    }

    if type.flags & AstFlags.Compound {
        compound_type_name := print_compound_type(type);
        compound_type := get_type(compound_type_name, type.file_index);
        if compound_type return compound_type;

        types: Array<TypeAst*>[type.generics.length];
        size: u32;
        flags := AstFlags.None;
        each generic, i in type.generics {
            has_generic := false;
            sub_type := verify_type(generic, &has_generic);
            if sub_type == null {
                return null;
            }
            else {
                size += sub_type.size;
                types[i] = sub_type;
                if sub_type.flags & AstFlags.Private {
                    flags = AstFlags.Private;
                }
            }
        }

        return create_compound_type(types, compound_type_name, size, flags, type.file_index);
    }

    if type.name == "Array" {
        if type.generics.length != 1 {
            report_error("Type 'Array' should have 1 generic type, but got %", type, type.generics.length);
            return null;
        }

        return verify_array(type, depth, is_generic);
    }
    if type.name == "CArray" {
        if type.generics.length != 1 {
            report_error("Type 'CArray' should have 1 generic type, but got %", type, type.generics.length);
            return null;
        }

        element_type := verify_type(type.generics[0], depth + 1);
        if element_type == null return null;

        array_length: s32;
        if initial_array_length {
            array_length = *initial_array_length;
        }
        else {
            is_constant := false;
            count_type := verify_expression(type.count, null, &is_constant, &array_length);
            if count_type == null || count_type.type_kind != TypeKind.Integer || !is_constant || array_length < 0 {
                report_error("Expected size of C array to be a constant, positive integer", type);
                return null;
            }
        }

        // Format the name for the type with [array_length] at the end
        length := integer_length(array_length);
        padding := length + 2;
        name := print_type_definition(type, padding);
        name[name.length - padding] = '[';
        array_length_temp := array_length;
        each i in 2..length + 1 {
            digit := array_length_temp % 10;
            name[name.length - i] = digit + '0';
            array_length_temp /= 10;
        }
        name[name.length - 1] = ']';

        array_type := cast(ArrayType*, get_type(name, type.file_index));
        if array_type == null {
            array_type = create_ast<ArrayType>(type, AstType.Array);
            array_type.name = allocate_string(name);
            array_type.size = element_type.size * array_length;
            array_type.alignment = element_type.alignment;
            array_type.flags |= AstFlags.IsType | (element_type.flags & AstFlags.Private);
            array_type.length = array_length;
            array_type.element_type = element_type;

            add_type(array_type);
            create_type_info(array_type);
        }
        return array_type;
    }

    if type.count != null {
        report_error("Type '%' cannot have a count", type, print_type_definition(type));
        return null;
    }

    has_generics := type.generics.length > 0;

    if type.name == "bool" {
        if has_generics
            report_error("Type 'bool' cannot have generics", type);

        return &bool_type;
    }
    if type.name == "string" {
        if has_generics
            report_error("Type 'string' cannot have generics", type);

        return string_type;
    }
    if type.name == "void" {
        if has_generics
            report_error("Type 'void' cannot have generics", type);

        return &void_type;
    }
    if type.name == "*" {
        if type.generics.length != 1 {
            report_error("Pointer type should have reference to 1 type, but got %", type, type.generics.length);
            return null;
        }

        pointer_type_name := print_type_definition(type);
        pointer_type := get_type(pointer_type_name, type.file_index);
        if pointer_type return pointer_type;

        pointed_to_type := verify_type(type.generics[0], is_generic, depth + 1);
        if pointed_to_type == null return null;

        // There are some cases where the pointed to type is a struct that contains a field for the pointer type
        // To account for this, the type table needs to be checked for again for the type
        pointer_type = get_type(pointer_type_name, type.file_index);
        if pointer_type == null
            pointer_type = create_pointer_type(pointer_type_name, pointed_to_type);

        return pointer_type;
    }
    if type.name == "..." {
        if has_generics
            report_error("Type 'varargs' cannot have generics", type);

        *is_varargs = true;
        return null;
    }
    if type.name == "Params" {
        if (!allow_params) return null;
        if depth != 0 {
            report_error("Params can only be declared as a top level type, such as 'Params<int>'", type);
            return null;
        }

        switch type.generics.length {
            case 0; {
                *is_params = true;
                array_any := "Array<Any>"; #const
                found, array_type := table_get(global_scope.identifiers, array_any);
                if found return cast(TypeAst*, array_type);

                return create_array_struct(array_any, any_type);
            }
            case 1; {
                *is_params = true;
                return verify_array(type, depth, is_generic);
            }
        }

        report_error("Type 'Params' should have 1 generic type, but got %", type, type.generics.length);
        return null;
    }
    if type.name == "Type" {
        if has_generics
            report_error("Type 'Type' cannot have generics", type);

        return &type_type;
    }
    if type.name == "Any" {
        if has_generics
            report_error("Type 'Any' cannot have generics", type);

        return any_type;
    }
    // if type.name == "Code" {
    //     if has_generics || depth > 0 || !allow_params
    //         report_error("Type 'Code' must be a standalone type used as an argument", type);
    //
    //     return &code_type;
    // }

    if has_generics {
        generic_name := print_type_definition(type);
        struct_type := get_type(generic_name, type.file_index);
        if struct_type return struct_type;

        struct_def := get_polymorphic_struct(type.name, type.file_index);
        if struct_def == null {
            report_error("No polymorphic structs of type '%'", type, type.name);
            return null;
        }

        if struct_def.generics.length != type.generics.length {
            report_error("Expected type '%' to have % generic(s), but got %", type, type.name, struct_def.generics.length, type.generics.length);
            return null;
        }

        generic_types: Array<TypeAst*>[type.generics.length];
        private_generic_types := false;

        each generic, i in type.generics {
            has_generic: bool;
            generic_type := verify_type(generic, &has_generic, depth + 1);
            if generic_type == null && !has_generic {
                return null;
            }
            else if has_generic {
                *is_generic = true;
            }
            else if generic_type.flags & AstFlags.Private {
                private_generic_types = true;
            }

            generic_types[i] = generic_type;
        }

        if is_generic return null;

        file_index := struct_def.file_index;
        if private_generic_types && (struct_def.flags & AstFlags.Private) != AstFlags.Private
            file_index = type.file_index;

        poly_struct := create_polymorphed_struct(struct_def, generic_name, TypeKind.Struct, private_generic_types, generic_types);
        add_type(poly_struct.name, poly_struct, file_index);
        verify_struct(poly_struct);
        return poly_struct;
    }

    type_value := get_type(type.name, type.file_index);
    if type_value {
        switch type_value.ast_type {
            case AstType.Struct; {
                struct_ast := cast(StructAst*, type_value);
                if (struct_ast.flags & AstFlags.Verifying) != AstFlags.Verifying verify_struct(struct_ast);
            }
            case AstType.Union; {
                union_ast := cast(UnionAst*, type_value);
                if (union_ast.flags & AstFlags.Verifying) != AstFlags.Verifying verify_union(union_ast);
            }
            case AstType.Interface; {
                interface_ast := cast(InterfaceAst*, type_value);
                if (interface_ast.flags & AstFlags.Verifying) != AstFlags.Verifying verify_interface(interface_ast);
            }
        }
    }

    return type_value;
}

string print_compound_type(TypeDefinition* type) #inline {
    length := -2; // Start at -2 to offset the last type not ending with ", "
    each sub_type in type.generics {
        length += determine_type_definition_length(sub_type) + 2; // Add 2 for the ", " after each sub-type
    }

    result_data: Array<u8>[length];
    result: string = { length = length; data = result_data.data; }
    add_generics_to_string(type, result, 0);

    return result;
}

string print_type_definition(TypeDefinition* type, int padding = 0) #inline {
    _: bool;
    return print_type_definition(type, &_, padding);
}

string print_type_definition(TypeDefinition* type, bool* allocated, int padding = 0) #inline {
    if type == null return "";

    if padding == 0 {
        if type.baked_type {
            *allocated = true;
            return type.baked_type.name;
        }
        if type.generics.length == 0 {
            *allocated = true;
            return type.name;
        }
    }

    // Determine how much space to reserve
    length := determine_type_definition_length(type) + padding;
    result_data: Array<u8>[length];
    result: string = { length = length; data = result_data.data; }
    add_type_to_string(type, result, 0);

    return result;
}

string print_operator(Operator op) {
    switch op {
        case Operator.And;              return "&&";
        case Operator.Or;               return "||";
        case Operator.Equality;         return "==";
        case Operator.NotEqual;         return "!=";
        case Operator.GreaterThanEqual; return ">=";
        case Operator.LessThanEqual;    return "<=";
        case Operator.ShiftLeft;        return "<<";
        case Operator.ShiftRight;       return ">>";
        case Operator.RotateLeft;       return "<<<";
        case Operator.RotateRight;      return ">>>";
        case Operator.Subscript;        return "[]";
        case Operator.Add;              return "+";
        case Operator.Subtract;         return "-";
        case Operator.Multiply;         return "*";
        case Operator.Divide;           return "/";
        case Operator.GreaterThan;      return ">";
        case Operator.LessThan;         return "<";
        case Operator.BitwiseOr;        return "|";
        case Operator.BitwiseAnd;       return "&";
        case Operator.Xor;              return "^";
        case Operator.Modulus;          return "%";
    }
    assert(false);
    return "";
}

base_array_type: StructAst*;
global_scope: GlobalScope;
private_scopes: Array<GlobalScope*>;
libraries: HashTable<string, Library*>;

#private

verify_constant_if_necessary(Ast* ast, TypeAst* type) {
    if ast.ast_type != AstType.Constant return;
    constant := cast(ConstantAst*, ast);

    if constant.type == null || constant.type == type || type.type_kind == TypeKind.Any return;

    error_format := "Type '%' cannot be converted to '%'"; #const
    out_of_range_error_format := "Value '%' out of range for type '%'"; #const
    switch constant.type.type_kind {
        case TypeKind.Integer; {
            switch type.type_kind {
                case TypeKind.Integer; {
                    if type.flags & AstFlags.Signed {
                        largest_allowed_value: s64 = 1 << (8 * type.size - 1) - 1;
                        if constant.type.flags & AstFlags.Signed {
                            lowest_allowed_value: s64 = -(1 << (8 * type.size - 1));
                            if constant.value.integer < lowest_allowed_value || constant.value.integer > largest_allowed_value
                                report_error(out_of_range_error_format, constant, constant.value.integer, type.name);
                        }
                        else if constant.value.integer > largest_allowed_value
                            report_error(out_of_range_error_format, constant, constant.value.integer, type.name);
                    }
                    else {
                        if (constant.type.flags & AstFlags.Signed) == AstFlags.Signed && constant.value.integer < 0
                            report_error("Value for unsigned type '%' cannot be negative", constant, type.name);
                        else {
                            largest_allowed_value: u64 = 1 << (8 * type.size) - 1;
                            if constant.value.unsigned_integer > largest_allowed_value
                                report_error(out_of_range_error_format, constant, constant.value.unsigned_integer, type.name);
                        }
                    }
                }
                // Convert int to float
                case TypeKind.Float; {
                    if constant.type.flags & AstFlags.Signed constant.value.double = cast(float64, constant.value.integer);
                    else constant.value.double = cast(float64, constant.value.unsigned_integer);
                }
                default; report_error(error_format, constant, constant.type.name, type.name);
            }
            constant.type = type;
        }
        case TypeKind.Float; {
            switch type.type_kind {
                // Convert float to int shelved for now
                // case TypeKind.Integer; {}
                case TypeKind.Float; {
                    if constant.type.flags & AstFlags.Signed constant.value.double = cast(float64, constant.value.integer);
                    else constant.value.double = cast(float64, constant.value.unsigned_integer);
                }
                default; report_error(error_format, constant, constant.type.name, type.name);
            }
            constant.type = type;
        }
    }
}

bool type_equals(TypeAst* target, TypeAst* source, bool check_primitives = false) {
    if target == null || source == null return false;
    if target == source return true;

    switch target.type_kind {
        case TypeKind.Integer; {
            if source.type_kind == TypeKind.Integer {
                if !check_primitives return true;
                return target.size == source.size && (target.flags & AstFlags.Signed) == (source.flags & AstFlags.Signed);
            }
        }
        case TypeKind.Float; {
            if source.type_kind == TypeKind.Float {
                if !check_primitives return true;
                return target.size == source.size;
            }
        }
        case TypeKind.Pointer; {
            if source.type_kind != TypeKind.Pointer return false;

            target_pointer := cast(PointerType*, target);
            target_pointer_type := target_pointer.pointed_type;
            source_pointer := cast(PointerType*, source);
            source_pointer_type := source_pointer.pointed_type;

            if target_pointer_type == &void_type || source_pointer_type == &void_type return true;

            if target_pointer_type.ast_type == AstType.Struct && source_pointer_type.ast_type == AstType.Struct {
                target_struct := cast(StructAst*, target_pointer_type);
                source_struct := cast(StructAst*, source_pointer_type);
                source_base_struct := source_struct.base_struct;

                while source_base_struct {
                    if target_struct == source_base_struct return true;

                    source_base_struct = source_base_struct.base_struct;
                }
            }
        }
        case TypeKind.Interface; {
            switch source.type_kind {
                // Cannot assign interfaces to interface types
                case TypeKind.Interface; return false;
                case TypeKind.Pointer; {
                    pointer_type := cast(PointerType*, source);
                    return pointer_type.pointed_type == &void_type;
                }
                case TypeKind.Function; {
                    interface_ast := cast(InterfaceAst*, target);
                    function_ast := cast(FunctionAst*, source);

                    if interface_ast.return_type == function_ast.return_type && interface_ast.arguments.length == function_ast.arguments.length {
                        each interface_arg, i in interface_ast.arguments {
                            function_arg := function_ast.arguments[i];
                            if interface_arg.type != function_arg.type return false;
                        }
                        return true;
                    }
                }
            }
        }
    }

    return false;
}

bool, FunctionAst* get_existing_function(string name, int file_index, int* count = null) {
    private_scope := private_scopes[file_index];

    if private_scope {
        found, functions := table_get(private_scope.functions, name);

        if found {
            function_count := functions.length;
            function := functions.data[0];

            found, functions = table_get(global_scope.functions, name);
            if found {
                function_count += functions.length;
            }

            if count { *count = function_count; }
            return true, function;
        }

        found, functions = table_get(global_scope.functions, name);

        if found {
            if count { *count = functions.length; }
            return true, functions.data[0];
        }
    }
    else {
        found, functions := table_get(global_scope.functions, name);

        if found {
            if count { *count = functions.length; }
            return true, functions.data[0];
        }
    }

    return false, null;
}

TypeAst* verify_array(TypeDefinition* type, int depth, bool* is_generic) {
    element_type_def := type.generics[0];
    element_type := verify_type(element_type_def, is_generic, depth + 1);
    if element_type == null return null;

    // Create a temporary string for the name of the array type
    name_data: Array<u8>[element_type.name.length + "Array<>".length];
    name: string = { length = name_data.length; data = name_data.data; }
    array_prefix := "Array<"; #const
    memory_copy(name.data, array_prefix.data, array_prefix.length);
    memory_copy(name.data + array_prefix.length, element_type.name.data, element_type.name.length);
    name[name.length - 1] = '>';

    array_type := get_type(name, type.file_index);
    if array_type return array_type;

    return create_array_struct(allocate_string(name), element_type, element_type_def);
}

TypeAst* create_array_struct(string name, TypeAst* element_type, TypeDefinition* element_type_def = null) {
    if base_array_type == null return null;

    array_struct := create_polymorphed_struct(base_array_type, name, TypeKind.Array, (element_type.flags & AstFlags.Private) == AstFlags.Private, element_type);
    add_type(name, array_struct, element_type.file_index);
    verify_struct(array_struct);
    return array_struct;
}

TypeAst* create_pointer_type(string name, TypeAst* pointed_to_type) {
    pointer_type: PointerType = { ast_type = AstType.Pointer; flags = AstFlags.IsType | (pointed_to_type.flags & AstFlags.Private); file_index = pointed_to_type.file_index; name = allocate_string(name); type_kind = TypeKind.Pointer; size = 8; alignment = 8; pointed_type = pointed_to_type; }

    type_pointer := new<PointerType>();
    *type_pointer = pointer_type;

    add_type(type_pointer);
    create_type_info(type_pointer);
    return type_pointer;
}

TypeAst* create_compound_type(Array<TypeAst*> types, string name, u32 size, AstFlags flags, int file_index) {
    compound_type: CompoundType = { ast_type = AstType.Compound; flags = AstFlags.IsType | flags; file_index = file_index; name = allocate_string(name); size = size; types = allocate_array(types); }

    type_pointer := new<CompoundType>();
    *type_pointer = compound_type;

    add_type(type_pointer);
    create_type_info(type_pointer);
    return type_pointer;
}

int determine_type_definition_length(TypeDefinition* type) {
    if type.baked_type return type.baked_type.name.length;
    if type.name == "*" return determine_type_definition_length(type.generics[0]) + 1;

    length := type.name.length;
    each generic in type.generics {
        length += determine_type_definition_length(generic) + 2; // Add 2 for the ", " after each generic
    }

    return length;
}

int add_type_to_string(TypeDefinition* type, string result, int index) {
    if type.baked_type {
        memory_copy(result.data + index, type.baked_type.name.data, type.baked_type.name.length);
        index += type.baked_type.name.length;
    }
    else if type.name == "*" {
        index = add_type_to_string(type.generics[0], result, index);
        result[index++] = '*';
    }
    else {
        memory_copy(result.data + index, type.name.data, type.name.length);
        index += type.name.length;
        if type.generics.length {
            result[index++] = '<';
            index = add_generics_to_string(type, result, index);
            result[index++] = '>';
        }
    }

    return index;
}

int add_generics_to_string(TypeDefinition* type, string result, int index) {
    i := 0;
    while i < type.generics.length - 1 {
        index = add_type_to_string(type.generics[i++], result, index);
        result[index++] = ',';
        result[index++] = ' ';
    }
    return add_type_to_string(type.generics[i], result, index);
}

string concat_temp(string l, string r) #inline {
    string_data: Array<u8>[l.length + r.length];
    result: string = { length = string_data.length; data = string_data.data; }
    memory_copy(result.data, l.data, l.length);
    memory_copy(result.data + l.length, r.data, r.length);

    return result;
}
