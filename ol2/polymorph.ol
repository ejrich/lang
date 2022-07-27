StructAst* create_polymorphed_struct(StructAst* base_struct, string name, TypeKind type_kind, bool private_generic_types, Params<TypeAst*> generic_types) {
    // var poly_struct = new StructAst
    // {
    //     FileIndex = baseStruct.FileIndex, Line = baseStruct.Line, Column = baseStruct.Column, Name = name,
    //     TypeKind = typeKind, Private = baseStruct.Private || privateGenericTypes,
    //     BaseStructName = baseStruct.Name, BaseTypeDefinition = baseStruct.BaseTypeDefinition,
    //     BaseStruct = baseStruct.BaseStruct, GenericTypes = genericTypes
    // };

    // foreach (var field in baseStruct.Fields)
    // {
    //     if (field.HasGenerics)
    //     {
    //         var newField = CopyAst(field);
    //         newField.TypeDefinition = CopyType(field.TypeDefinition, genericTypes);
    //         newField.Name = field.Name;
    //         newField.Value = field.Value;
    //         newField.Assignments = field.Assignments;
    //         newField.ArrayValues = field.ArrayValues;
    //         poly_struct.Fields.Add(newField);
    //     }
    //     else
    //     {
    //         poly_struct.Fields.Add(field);
    //     }
    // }

    // return poly_struct;
    return null;
}
