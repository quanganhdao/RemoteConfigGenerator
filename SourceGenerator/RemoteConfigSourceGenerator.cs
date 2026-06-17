using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RemoteConfigGenerator
{
    [Generator]
    public class RemoteConfigSourceGenerator : ISourceGenerator
    {
        private const string AttributeNamespace = "RemoteConfigGenerator";
        private const string RemoteConfigDataAttributeName = "RemoteConfigDataAttribute";
        private const string RemoteConfigFieldAttributeName = "RemoteConfigFieldAttribute";

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new RemoteConfigSyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            // ✅ Generate attributes first so they're available for user code
            context.AddSource("RemoteConfigAttributes.g.cs", SourceText.From(@"
using System;

namespace RemoteConfigGenerator
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class RemoteConfigDataAttribute : Attribute
    {
        public string PrefsPrefix { get; set; } = ""rc_"";
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class RemoteConfigFieldAttribute : Attribute
    {
        public string Key { get; set; }
        public bool PersistToPrefs { get; set; } = true;
        public bool SyncFromRemote { get; set; } = true;

        public RemoteConfigFieldAttribute() { }
        public RemoteConfigFieldAttribute(string key) { Key = key; }
    }
}
", Encoding.UTF8));

            if (!(context.SyntaxReceiver is RemoteConfigSyntaxReceiver receiver))
                return;

            foreach (var candidateClass in receiver.CandidateClasses)
            {
                var model = context.Compilation.GetSemanticModel(candidateClass.SyntaxTree);
                var classSymbol = model.GetDeclaredSymbol(candidateClass);

                if (classSymbol == null)
                    continue;

                // Check if class has RemoteConfigData attribute
                var hasAttribute = classSymbol.GetAttributes()
                    .Any(a => a.AttributeClass?.Name == RemoteConfigDataAttributeName);

                if (!hasAttribute)
                    continue;

                var source = GenerateRemoteConfigExtensions(classSymbol, context);
                context.AddSource($"{classSymbol.Name}_Generated.g.cs", SourceText.From(source, Encoding.UTF8));
            }
        }

        private string GenerateRemoteConfigExtensions(INamedTypeSymbol classSymbol, GeneratorExecutionContext context)
        {
            var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : classSymbol.ContainingNamespace.ToDisplayString();

            var className = classSymbol.Name;
            var prefsPrefix = GetPrefsPrefix(classSymbol);

            // Collect all fields and properties with RemoteConfigField attribute
            // ✅ IMPROVED: Tự động scan TẤT CẢ public static fields nếu không có [RemoteConfigField]
            var members = new List<ConfigMember>();

            // Đếm số fields có [RemoteConfigField]
            int fieldsWithAttribute = 0;
            foreach (var member in classSymbol.GetMembers())
            {
                var attribute = member.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == RemoteConfigFieldAttributeName);
                if (attribute != null) fieldsWithAttribute++;
            }

            // Nếu KHÔNG có field nào có [RemoteConfigField] → Auto-scan tất cả public static fields
            bool autoScanMode = fieldsWithAttribute == 0;

            foreach (var member in classSymbol.GetMembers())
            {
                var attribute = member.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == RemoteConfigFieldAttributeName);

                // ✅ AUTO-SCAN MODE: Nếu không có field nào có attribute, tự động scan ALL fields
                bool shouldInclude = false;
                
                if (autoScanMode)
                {
                    // Auto-scan: Chỉ lấy public static fields/properties
                    if (member.IsStatic && member.DeclaredAccessibility == Accessibility.Public)
                    {
                        shouldInclude = true;
                    }
                }
                else
                {
                    // Manual mode: Chỉ lấy fields có [RemoteConfigField]
                    shouldInclude = (attribute != null);
                }

                if (!shouldInclude)
                    continue;

                ConfigMember configMember = null;

                if (member is IFieldSymbol field)
                {
                    configMember = new ConfigMember
                    {
                        Name = field.Name,
                        Type = field.Type.ToDisplayString(),
                        IsField = true
                    };
                }
                else if (member is IPropertySymbol property)
                {
                    configMember = new ConfigMember
                    {
                        Name = property.Name,
                        Type = property.Type.ToDisplayString(),
                        IsField = false
                    };
                }

                if (configMember != null)
                {
                    // Default values
                    configMember.Key = configMember.Name;
                    configMember.PersistToPrefs = true;
                    configMember.SyncFromRemote = true;

                    // Override with attribute values if present
                    if (attribute != null)
                    {
                        // ✅ FIX: Try semantic model first, then fallback to syntax parsing
                        var customKey = GetAttributePropertyValue(attribute, "Key") as string;
                        if (!string.IsNullOrEmpty(customKey))
                        {
                            configMember.Key = customKey;
                        }
                        else
                        {
                            // Fallback: Parse syntax directly (works even if attribute is generated in same pass)
                            var syntaxKey = GetAttributeKeyFromSyntax(member);
                            if (!string.IsNullOrEmpty(syntaxKey))
                                configMember.Key = syntaxKey;
                        }

                        var persistToPrefs = GetAttributePropertyValue(attribute, "PersistToPrefs") as bool?;
                        if (persistToPrefs.HasValue)
                            configMember.PersistToPrefs = persistToPrefs.Value;

                        var syncFromRemote = GetAttributePropertyValue(attribute, "SyncFromRemote") as bool?;
                        if (syncFromRemote.HasValue)
                            configMember.SyncFromRemote = syncFromRemote.Value;
                    }

                    members.Add(configMember);
                }
            }

            var sb = new StringBuilder();

            // File header
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine($"// Generated for: {className}");
            sb.AppendLine($"// Total fields: {members.Count}");
            sb.AppendLine($"// Generated at: {System.DateTime.Now}");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("#if VIRTUESKY_FIREBASE_REMOTECONFIG");
            sb.AppendLine("using Firebase.RemoteConfig;");
            sb.AppendLine("#endif");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine($"namespace {namespaceName}");
                sb.AppendLine("{");
            }

            // Generate partial class
            sb.AppendLine($"    public static partial class {className}Extensions");
            sb.AppendLine("    {");
            
            // Generate FieldSetterLookup
            GenerateFieldSetterLookup(sb, className, members);
            sb.AppendLine();

            // Generate FieldGetterLookup
            GenerateFieldGetterLookup(sb, className, members);
            sb.AppendLine();

            // Generate SetFieldValue_Generated
            GenerateSetFieldValue(sb, className, members);
            sb.AppendLine();

            // Generate GetFieldValue_Generated
            GenerateGetFieldValue(sb, className, members);
            sb.AppendLine();

            // Generate ExportToString_Generated
            GenerateExportToString(sb, className, members);
            sb.AppendLine();

            // Generate local parsing helpers
            GenerateParsingHelpers(sb);

            sb.AppendLine("    }");

            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private void GenerateFieldSetterLookup(StringBuilder sb, string className, List<ConfigMember> members)
        {
            sb.AppendLine($"        public static readonly Dictionary<string, Action<string>> FieldSetterLookup = new Dictionary<string, Action<string>>");
            sb.AppendLine("        {");

            foreach (var member in members.Where(m => m.SyncFromRemote))
            {
                sb.AppendLine($"            {{ \"{member.Key}\", value => {{");

                var typeName = member.Type;
                if (typeName == "string" || typeName == "System.String")
                {
                    sb.AppendLine($"                {className}.{member.Name} = value;");
                }
                else if (typeName == "int" || typeName == "System.Int32")
                {
                    sb.AppendLine($"                if (int.TryParse(value, out var result)) {className}.{member.Name} = result;");
                }
                else if (typeName == "float" || typeName == "System.Single")
                {
                    sb.AppendLine($"                if (float.TryParse(value, out var result)) {className}.{member.Name} = result;");
                }
                else if (typeName == "bool" || typeName == "System.Boolean")
                {
                    sb.AppendLine($"                {className}.{member.Name} = value == \"true\" || value == \"1\" || value.ToLower() == \"true\";");
                }
                else if (typeName == "long" || typeName == "System.Int64")
                {
                    sb.AppendLine($"                if (long.TryParse(value, out var result)) {className}.{member.Name} = result;");
                }
                else if (typeName == "int[]" || typeName == "System.Int32[]")
                {
                    sb.AppendLine($"                {className}.{member.Name} = ParseIntArray(value);");
                }
                else if (typeName == "float[]" || typeName == "System.Single[]")
                {
                    sb.AppendLine($"                {className}.{member.Name} = ParseFloatArray(value);");
                }

                sb.AppendLine("            }},");
            }

            sb.AppendLine("        };");
        }

        private void GenerateFieldGetterLookup(StringBuilder sb, string className, List<ConfigMember> members)
        {
            sb.AppendLine($"        public static readonly Dictionary<string, Func<object>> FieldGetterLookup = new Dictionary<string, Func<object>>");
            sb.AppendLine("        {");

            foreach (var member in members)
            {
                sb.AppendLine($"            {{ \"{member.Key}\", () => {className}.{member.Name} }},");
            }

            sb.AppendLine("        };");
        }

        private void GenerateSetFieldValue(StringBuilder sb, string className, List<ConfigMember> members)
        {
            sb.AppendLine("#if VIRTUESKY_FIREBASE_REMOTECONFIG");
            sb.AppendLine("        public static bool SetFieldValue_Generated(string fieldName, ConfigValue configValue)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (fieldName)");
            sb.AppendLine("            {");

            foreach (var member in members.Where(m => m.SyncFromRemote))
            {
                sb.AppendLine($"                case \"{member.Key}\":");

                var typeName = member.Type;
                if (typeName == "string" || typeName == "System.String")
                {
                    sb.AppendLine($"                    {className}.{member.Name} = configValue.StringValue;");
                }
                else if (typeName == "int" || typeName == "System.Int32")
                {
                    sb.AppendLine($"                    {className}.{member.Name} = (int)configValue.LongValue;");
                }
                else if (typeName == "float" || typeName == "System.Single")
                {
                    sb.AppendLine($"                    {className}.{member.Name} = (float)configValue.DoubleValue;");
                }
                else if (typeName == "bool" || typeName == "System.Boolean")
                {
                    sb.AppendLine($"                    {className}.{member.Name} = configValue.BooleanValue || configValue.StringValue == \"true\" || configValue.StringValue == \"1\";");
                }
                else if (typeName == "long" || typeName == "System.Int64")
                {
                    sb.AppendLine($"                    {className}.{member.Name} = configValue.LongValue;");
                }

                sb.AppendLine("                    return true;");
            }

            sb.AppendLine("                default:");
            sb.AppendLine("                    return false;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("#endif");
        }

        private void GenerateGetFieldValue(StringBuilder sb, string className, List<ConfigMember> members)
        {
            sb.AppendLine("        public static object GetFieldValue_Generated(string fieldName)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (fieldName)");
            sb.AppendLine("            {");

            foreach (var member in members)
            {
                sb.AppendLine($"                case \"{member.Key}\":");
                sb.AppendLine($"                    return {className}.{member.Name};");
            }

            sb.AppendLine("                default:");
            sb.AppendLine("                    return null;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
        }

        private void GenerateExportToString(StringBuilder sb, string className, List<ConfigMember> members)
        {
            sb.AppendLine("        public static string ExportToString_Generated()");
            sb.AppendLine("        {");
            sb.AppendLine("            var sb = new System.Text.StringBuilder();");

            foreach (var member in members.OrderBy(m => m.Name))
            {
                sb.AppendLine($"            {{");
                
                // ✅ FIX: Xử lý riêng cho value types và reference types
                var typeName = member.Type;
                if (typeName == "string" || typeName == "System.String")
                {
                    // String có thể null
                    sb.AppendLine($"                var value = {className}.{member.Name} ?? \"null\";");
                }
                else
                {
                    // Value types (int, bool, float, etc.) không thể null
                    sb.AppendLine($"                var value = {className}.{member.Name}.ToString();");
                }
                
                sb.AppendLine($"                if (value.Length > 300)");
                sb.AppendLine($"                    sb.AppendLine(\"{member.Name}: <color='#FF0000'>\" + value.Substring(0, 300) + \"...</color>\");");
                sb.AppendLine($"                else");
                sb.AppendLine($"                    sb.AppendLine(\"{member.Name}: <color='#FF0000'>\" + value + \"</color>\");");
                sb.AppendLine($"            }}");
            }

            sb.AppendLine("            return sb.ToString();");
            sb.AppendLine("        }");
        }

        private void GenerateParsingHelpers(StringBuilder sb)
        {
            sb.AppendLine("        private static int[] ParseIntArray(string value)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (string.IsNullOrEmpty(value)) return new int[0];");
            sb.AppendLine("            var parts = value.Split(',');");
            sb.AppendLine("            var values = new List<int>();");
            sb.AppendLine("            foreach (var part in parts)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (int.TryParse(part.Trim(), out var parsed)) values.Add(parsed);");
            sb.AppendLine("            }");
            sb.AppendLine("            return values.ToArray();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static float[] ParseFloatArray(string value)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (string.IsNullOrEmpty(value)) return new float[0];");
            sb.AppendLine("            var parts = value.Split(',');");
            sb.AppendLine("            var values = new List<float>();");
            sb.AppendLine("            foreach (var part in parts)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (float.TryParse(part.Trim(), out var parsed)) values.Add(parsed);");
            sb.AppendLine("            }");
            sb.AppendLine("            return values.ToArray();");
            sb.AppendLine("        }");
        }

        private string GetPrefsPrefix(INamedTypeSymbol classSymbol)
        {
            var attribute = classSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == RemoteConfigDataAttributeName);

            if (attribute != null)
            {
                var prefix = GetAttributePropertyValue(attribute, "PrefsPrefix") as string;
                if (!string.IsNullOrEmpty(prefix))
                    return prefix;
            }

            return "rc_";
        }

        private object GetAttributePropertyValue(AttributeData attribute, string propertyName)
        {
            // Check named arguments (e.g., Key = "value")
            foreach (var namedArg in attribute.NamedArguments)
            {
                if (namedArg.Key == propertyName)
                    return namedArg.Value.Value;
            }

            // Check constructor arguments for "Key" property specifically
            if (propertyName == "Key" && attribute.ConstructorArguments.Length > 0)
            {
                return attribute.ConstructorArguments[0].Value;
            }

            return null;
        }

        /// <summary>
        /// Parse attribute Key directly from syntax tree (fallback when semantic model doesn't work)
        /// Handles: [RemoteConfigField(Key = "VALUE")] and [RemoteConfigField("VALUE")]
        /// </summary>
        private string GetAttributeKeyFromSyntax(ISymbol member)
        {
            foreach (var syntaxRef in member.DeclaringSyntaxReferences)
            {
                var syntax = syntaxRef.GetSyntax();

                // Find attribute list - IMPORTANT: Fields use VariableDeclaratorSyntax
                SyntaxList<AttributeListSyntax> attributeLists = default;

                // ✅ FIX: For fields, the syntax is VariableDeclaratorSyntax, need to go up to FieldDeclarationSyntax
                if (syntax is VariableDeclaratorSyntax variableDeclarator)
                {
                    // Go up: VariableDeclaratorSyntax -> VariableDeclarationSyntax -> FieldDeclarationSyntax
                    if (variableDeclarator.Parent?.Parent is FieldDeclarationSyntax fieldDecl)
                    {
                        attributeLists = fieldDecl.AttributeLists;
                    }
                }
                else if (syntax is FieldDeclarationSyntax fieldSyntax)
                {
                    attributeLists = fieldSyntax.AttributeLists;
                }
                else if (syntax is PropertyDeclarationSyntax propertySyntax)
                {
                    attributeLists = propertySyntax.AttributeLists;
                }

                // Parse each attribute
                foreach (var attributeList in attributeLists)
                {
                    foreach (var attr in attributeList.Attributes)
                    {
                        var attrName = attr.Name.ToString();
                        if (attrName == "RemoteConfigField" || attrName == "RemoteConfigFieldAttribute")
                        {
                            if (attr.ArgumentList != null)
                            {
                                foreach (var arg in attr.ArgumentList.Arguments)
                                {
                                    // Case 1: Named argument [RemoteConfigField(Key = "VALUE")]
                                    if (arg.NameEquals != null && arg.NameEquals.Name.ToString() == "Key")
                                    {
                                        // ✅ FIX: Use LiteralExpressionSyntax to get actual value without quotes
                                        if (arg.Expression is LiteralExpressionSyntax literal)
                                        {
                                            return literal.Token.ValueText;
                                        }
                                        // Fallback: trim quotes manually
                                        return arg.Expression.ToString().Trim('"');
                                    }
                                    // Case 2: Constructor argument [RemoteConfigField("VALUE")]
                                    else if (arg.NameEquals == null && arg.Expression is LiteralExpressionSyntax literal)
                                    {
                                        return literal.Token.ValueText;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        private class ConfigMember
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string Key { get; set; }
            public bool IsField { get; set; }
            public bool PersistToPrefs { get; set; }
            public bool SyncFromRemote { get; set; }
        }

        private class RemoteConfigSyntaxReceiver : ISyntaxReceiver
        {
            public List<ClassDeclarationSyntax> CandidateClasses { get; } = new List<ClassDeclarationSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (syntaxNode is ClassDeclarationSyntax classDeclaration &&
                    classDeclaration.AttributeLists.Count > 0)
                {
                    CandidateClasses.Add(classDeclaration);
                }
            }
        }
    }
}
