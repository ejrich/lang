#import "ir_builder.ol"
#import "type_table.ol"

init_types() {
}

init_necessary_types() {
}

verify_compiler_directives() {
    // Verify compiler directives, collecting run directives to be handled afterwards

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

bool add_global_variable(DeclarationAst* declaration) {
    return true;
}

add_function(FunctionAst* function) {
}

bool add_polymorphic_struct(StructAst* struct_ast) {
    return true;
}

bool add_struct(StructAst* struct_ast) {
    return true;
}

add_enum(EnumAst* enum_ast) {
}

add_union(UnionAst* union_ast) {
}

add_library(CompilerDirectiveAst* directive) {
}

add_system_library(CompilerDirectiveAst* directive) {
}

add_overload(OperatorOverloadAst* overload) {
}

add_interface(InterfaceAst* interface_ast) {
}

TypeAst* verify_type(TypeDefinition* type, Scope* scope) {
    isGeneric := false;
    isVarargs := false;
    isParams := false;

    if type == null return null;
    if type.BakedType return type.BakedType;

    if type.is_generic {
        if type.generics.length
            report_error("Generic type cannot have additional generic types", type);

        isGeneric = true;
        return null;
    }

    if type.compound {
        compound_type_name := print_compound_type(type);
        compound_type := GetType(compound_type_name, type.file_index);
        if compound_type
            return compoundType;

        types: Array<TypeAst*>[type.generics.length];
        size: u32;
        each generic, i in type.generics {
            sub_type = verify_type(generic, scope);
            if sub_type == null {
                return null;
            }
            else {
                size += sub_type.size;
                types[i] = sub_type;
                if sub_type.private {
                    private_type = true;
                }
            }
        }

        return create_compound_type(types, compound_type_name, size, private_type, type.file_index);
    }

    if type.name == "Array" {
        if type.generics.length != 1 {
            report_error("Type 'Array' should have 1 generic type, but got %", type, type.generics.length);
            return null;
        }

        return VerifyArray(type, scope, depth);
    }
    if type.name == "CArray" {
        if type.generics.length != 1 {
            report_error("Type 'CArray' should have 1 generic type, but got %", type, type.generics.length);
            return null;
        }

        element_type := verify_type(type.Generics[0], scope, depth + 1, allowParams);
        if element_type == null {
            return null;
        }

        array_length: u32;
        if initial_array_length {
            array_length = *initial_array_length;
        }
        else {
            is_constant := false;
            count_type := verify_expression(type.count, null, scope, &is_constant, &array_length);
            if count_type == null || count_type.TypeKind != TypeKind.Integer || !is_constant || array_length < 0 {
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

        array_type := get_type(name, type.FileIndex);
        if array_type == null {
            array_type = create_ast<ArrayType>(null, AstType.Array);
            array_type.name = name;
            array_type.size = element_type.Size * array_length;
            array_type.alignment = element_type.alignment;
            array_type.private = element_type.private;
            array_type.length = array_length;
            array_type.element_type = element_type;

            add_type(name, array_type);
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

        pointer_type_name := PrintTypeDefinition(type);
        pointer_type := get_type(pointerTypeName, type.file_index);
        if pointer_type return pointerType;

        pointed_to_type = verify_type(type.generics[0], scope, &is_generic, depth + 1, allow_params);
        if pointedToType == null
            return null;

        // There are some cases where the pointed to type is a struct that contains a field for the pointer type
        // To account for this, the type table needs to be checked for again for the type
        pointer_type := get_type(pointerTypeName, type.file_index);
        if pointer_type == null {
            pointer_type = create_pointer_type(pointerTypeName, pointedToType);
        }
        return pointer_type;
    }
    if type.name == "..." {
        if has_generics
            report_error("Type 'varargs' cannot have generics", type);

        isVarargs = true;
        return null;
    }
    if type.name == "Params" {
        if (!allowParams) return null;
        if depth != 0 {
            report_error("Params can only be declared as a top level type, such as 'Params<int>'", type);
            return null;
        }

        switch type.generics.length {
            case 0; {
                isParams = true;
                arrayAny := "Array<Any>"; #const
                if (GlobalScope.Types.TryGetValue(arrayAny, arrayType))
                {
                    return arrayType;
                }

                return CreateArrayStruct(arrayAny, TypeTable.AnyType);
            }
            case 1; {
                isParams = true;
                return VerifyArray(type, scope, depth, &isGeneric);
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

        return &any_type;
    }
    // case "Code":
    //     if (has_generics || depth > 0 || !allowParams)
    //     {
    //         report_error("Type 'Code' must be a standalone type used as an argument", type);
    //     }
    //     return TypeTable.CodeType;

    if has_generics {
        genericName := PrintTypeDefinition(type);
        if (GetType(genericName, type.FileIndex, out var structType))
        {
            return structType;
        }

        if (!GetPolymorphicStruct(type.Name, type.FileIndex, out var structDef))
        {
            report_error($"No polymorphic structs of type '{type.Name}'", type);
            return null;
        }

        var generics = type.Generics.ToArray();
        var genericTypes = new IType[generics.Length];
        var error = false;
        var privateGenericTypes = false;

        for (var i = 0; i < generics.Length; i++)
        {
            var genericType = genericTypes[i] = verify_type(generics[i], scope, out var hasGeneric, out _, out _, depth + 1, allowParams);
            if (genericType == null && !hasGeneric)
            {
                error = true;
            }
            else if (hasGeneric)
            {
                isGeneric = true;
            }
            else if (genericType.Private)
            {
                privateGenericTypes = true;
            }
        }

        if (structDef.Generics.Count != type.Generics.Count)
        {
            report_error($"Expected type '{type.Name}' to have {structDef.Generics.Count} generic(s), but got {type.Generics.Count}", type);
            return null;
        }

        if (error || isGeneric)
        {
            return null;
        }

        var fileIndex = structDef.FileIndex;
        if (privateGenericTypes && !structDef.Private)
        {
            fileIndex = type.FileIndex;
        }

        var polyStruct = Polymorpher.CreatePolymorphedStruct(structDef, genericName, TypeKind.Struct, privateGenericTypes, genericTypes);
        AddType(genericName, polyStruct, fileIndex);
        VerifyStruct(polyStruct);
        return polyStruct;
    }
    else if (GetType(type.Name, type.FileIndex, out var typeValue))
    {
        if (typeValue is StructAst structAst)
        {
            if (!structAst.Verifying)
            {
                VerifyStruct(structAst);
            }
        }
        else if (typeValue is UnionAst union)
        {
            if (!union.Verifying)
            {
                VerifyUnion(union);
            }
        }
        else if (typeValue is InterfaceAst interfaceAst)
        {
            if (!interfaceAst.Verifying)
            {
                VerifyInterface(interfaceAst);
            }
        }
        return typeValue;
    }
    return null;
}

string print_type_definition(TypeDefinition* type, int padding = 0) {
    if type == null return "";

    if padding == 0 {
        if type.baked_type return type.baked_type.name;
        if type.generics.length == 0 return type.name;
    }

    // Determine how much space to reserve
    length := determine_type_definition_length(type) + padding;

    result: string = { length = length; data = allocate(length); }
    add_type_to_string(type, result, 0);

    return result;
}

base_array_type: StructAst*;
global_scope: GlobalScope;
private_scopes: Array<GlobalScope*>;

#private

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
            i := 0;
            while i < type.generics.length - 1 {
                index = add_type_to_string(type.generics[i++], result, index);
                result[index++] = ',';
                result[index++] = ' ';
            }
            index = add_type_to_string(type.generics[i], result, index);
            result[index++] = '>';
        }
    }

    return index;
}
