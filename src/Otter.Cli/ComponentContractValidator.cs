using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Otter.Language;

namespace Otter.Cli;

public record ContractDiagnostic(
    string Level,
    string Code,
    string Message,
    string? File = null,
    int Line = 0,
    string? Suggestion = null
);

public class ContractValidationResult
{
    public bool IsValid { get; set; } = true;
    public string PackId { get; set; } = "";
    public string Version { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public string? VariablePrefix { get; set; }
    public string PackDir { get; set; } = "";
    public string VerificationScope { get; set; } = "structural_ast";
    public string BehavioralVerification { get; set; } = "unproven_contract_specification";
    public string BehavioralNote { get; set; } = "Declared return types, side effects, and error modes are machine-readable contract specifications for composition planning. They are not proven behavioral invariants.";
    public List<string> StructuralChecks { get; } = new()
    {
        "manifest_schema",
        "ast_export_existence",
        "parameter_count",
        "parameter_names",
        "ast_dependency_declarations",
        "variable_prefix_collision_mitigation"
    };
    public List<ContractDiagnostic> Diagnostics { get; } = new();
    public List<string> ValidatedOperations { get; } = new();

    public void AddError(string code, string message, string? file = null, int line = 0, string? suggestion = null)
    {
        IsValid = false;
        Diagnostics.Add(new ContractDiagnostic("ERROR", code, message, file, line, suggestion));
    }

    public void AddWarning(string code, string message, string? file = null, int line = 0, string? suggestion = null)
    {
        Diagnostics.Add(new ContractDiagnostic("WARNING", code, message, file, line, suggestion));
    }

    public JsonObject ToJsonObject()
    {
        var diagArray = new JsonArray();
        foreach (var d in Diagnostics)
        {
            var dObj = new JsonObject
            {
                ["level"] = d.Level,
                ["code"] = d.Code,
                ["message"] = d.Message
            };
            if (d.File != null) dObj["file"] = d.File;
            if (d.Line > 0) dObj["line"] = d.Line;
            if (d.Suggestion != null) dObj["suggestion"] = d.Suggestion;
            diagArray.Add(dObj);
        }

        var opsArray = new JsonArray();
        foreach (var op in ValidatedOperations)
        {
            opsArray.Add((JsonNode)JsonValue.Create(op)!);
        }

        var checksArray = new JsonArray();
        foreach (var check in StructuralChecks)
        {
            checksArray.Add((JsonNode)JsonValue.Create(check)!);
        }

        return new JsonObject
        {
            ["pack"] = PackId,
            ["version"] = Version,
            ["schemaVersion"] = SchemaVersion,
            ["variablePrefix"] = VariablePrefix ?? "",
            ["status"] = IsValid ? "VALID" : "INVALID",
            ["verificationScope"] = VerificationScope,
            ["behavioralVerification"] = BehavioralVerification,
            ["behavioralNote"] = BehavioralNote,
            ["structuralChecks"] = checksArray,
            ["operationsCount"] = ValidatedOperations.Count,
            ["validatedOperations"] = opsArray,
            ["diagnostics"] = diagArray
        };
    }
}

public static class ComponentContractValidator
{
    private static readonly Regex SemverRegex = new(@"^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.]+)?$", RegexOptions.Compiled);
    private static readonly Regex KebabRegex = new(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex VariablePrefixRegex = new(@"^_[a-z0-9]{2,4}_$", RegexOptions.Compiled);
    private static readonly Regex UseLineRegex = new(@"^\s*use\s+""([^""]+)""\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedParamTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "number", "boolean", "list", "thing", "any", "object"
    };

    private static readonly HashSet<string> AllowedReturnTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "number", "boolean", "list", "thing", "any", "object", "void"
    };

    private static readonly HashSet<string> AllowedSideEffects = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "filesystem:read", "filesystem:write", "network:http", "console:output", "state:mutation"
    };

    private static readonly HashSet<string> AllowedErrorModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "returns-gone", "returns-false", "returns-fallback", "throws-error"
    };

    private static readonly HashSet<string> AllowedKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "pack", "kit", "blueprint", "recipe"
    };

    public static ContractValidationResult ValidatePackDirectory(string packDir)
    {
        var result = new ContractValidationResult
        {
            PackDir = Path.GetFullPath(packDir)
        };

        string manifestPath = Path.Combine(packDir, "otter.pack.json");
        if (!File.Exists(manifestPath))
        {
            result.AddError("MANIFEST_NOT_FOUND", $"Component manifest otter.pack.json not found in '{packDir}'.", manifestPath);
            return result;
        }

        string manifestContent;
        try
        {
            manifestContent = File.ReadAllText(manifestPath);
        }
        catch (Exception ex)
        {
            result.AddError("MANIFEST_READ_ERROR", $"Failed to read manifest file: {ex.Message}", manifestPath);
            return result;
        }

        JsonNode? parsedNode;
        try
        {
            parsedNode = JsonNode.Parse(manifestContent);
        }
        catch (JsonException jex)
        {
            result.AddError("MALFORMED_JSON", $"Malformed manifest JSON in otter.pack.json: {jex.Message}", manifestPath);
            return result;
        }

        if (parsedNode is not JsonObject manifest)
        {
            result.AddError("INVALID_MANIFEST", "Manifest must be a JSON object.", manifestPath);
            return result;
        }

        // 1. Core Identification & Metadata
        string name = manifest["name"]?.ToString() ?? "";
        string publisher = manifest["publisher"]?.ToString() ?? "";
        string version = manifest["version"]?.ToString() ?? "";
        string kind = manifest["kind"]?.ToString() ?? "pack";
        string description = manifest["description"]?.ToString() ?? "";
        string entry = manifest["entry"]?.ToString() ?? "src/main.ot";
        string license = manifest["license"]?.ToString() ?? "";

        result.PackId = string.IsNullOrEmpty(publisher) ? name : $"{publisher}/{name}";
        result.Version = version;

        if (manifest["schemaVersion"] == null)
        {
            result.AddError("MISSING_SCHEMA_VERSION", "Manifest is missing required 'schemaVersion'.", manifestPath);
        }
        else
        {
            if (int.TryParse(manifest["schemaVersion"]?.ToString(), out int sVer))
            {
                result.SchemaVersion = sVer;
            }
            else
            {
                result.AddError("INVALID_SCHEMA_VERSION", "Property 'schemaVersion' must be an integer.", manifestPath);
            }
        }

        if (string.IsNullOrWhiteSpace(name) || !KebabRegex.IsMatch(name))
        {
            result.AddError("INVALID_NAME", $"Component name '{name}' must be kebab-case (e.g. 'text-utils').", manifestPath);
        }
        if (string.IsNullOrWhiteSpace(publisher) || !KebabRegex.IsMatch(publisher))
        {
            result.AddError("INVALID_PUBLISHER", $"Publisher namespace '{publisher}' must be kebab-case (e.g. 'otter-official').", manifestPath);
        }
        if (string.IsNullOrWhiteSpace(version) || !SemverRegex.IsMatch(version))
        {
            result.AddError("INVALID_VERSION", $"Version '{version}' must be a valid semantic version (e.g. '1.0.1').", manifestPath);
        }
        if (!AllowedKinds.Contains(kind))
        {
            result.AddError("INVALID_KIND", $"Unknown component kind '{kind}'. Allowed: pack, kit, blueprint, recipe.", manifestPath);
        }
        if (string.IsNullOrWhiteSpace(description))
        {
            result.AddError("MISSING_DESCRIPTION", "Manifest requires a non-empty 'description'.", manifestPath);
        }
        if (string.IsNullOrWhiteSpace(license))
        {
            result.AddError("MISSING_LICENSE", "Manifest requires a non-empty 'license' identifier (e.g. 'MIT').", manifestPath);
        }
        if (string.IsNullOrWhiteSpace(entry) || !entry.StartsWith("src/") || !entry.EndsWith(".ot"))
        {
            result.AddError("INVALID_ENTRY", $"Entry point '{entry}' must be a relative Otter source path (e.g. 'src/main.ot').", manifestPath);
        }

        // Check runtime constraint
        if (manifest["requiresOtter"] != null)
        {
            string reqOtter = manifest["requiresOtter"]!.ToString().Trim();
            if (string.IsNullOrWhiteSpace(reqOtter) || (!reqOtter.StartsWith("^") && !reqOtter.StartsWith(">=") && !reqOtter.StartsWith("~") && !SemverRegex.IsMatch(reqOtter)))
            {
                result.AddError("INVALID_RUNTIME_CONSTRAINT", $"Invalid requiresOtter constraint '{reqOtter}'. Expected semver constraint like '>=1.0.1' or '^1.0.1'.", manifestPath);
            }
        }

        // Check variable prefix
        if (manifest["variablePrefix"] != null)
        {
            string prefix = manifest["variablePrefix"]!.ToString().Trim();
            result.VariablePrefix = prefix;
            if (!VariablePrefixRegex.IsMatch(prefix))
            {
                result.AddError("INVALID_VARIABLE_PREFIX", $"Variable prefix '{prefix}' must match pattern '^_[a-z0-9]{{2,4}}_$' (e.g. '_tu_', '_mu_').", manifestPath);
            }
        }

        // Schema v1 check
        if (result.SchemaVersion < 2 && manifest["provides"] == null)
        {
            result.AddError("SCHEMA_V1_UNCONTRACTED", $"Component '{result.PackId}' uses Schema v1 without component contract. Migrate manifest to schemaVersion: 2 with 'provides.operations' to enable contract verification.", manifestPath, suggestion: "Set \"schemaVersion\": 2 and add \"provides\": { \"operations\": [...] } to otter.pack.json");
            return result;
        }

        // 2. Validate provides.operations
        if (manifest["provides"] is not JsonObject providesObj)
        {
            result.AddError("MISSING_PROVIDES", "Schema v2 component contract requires a 'provides' object.", manifestPath);
            return result;
        }

        if (providesObj["operations"] is not JsonArray operationsArray || operationsArray.Count == 0)
        {
            result.AddError("EMPTY_OPERATIONS", "Component contract requires at least one public operation declared in 'provides.operations'.", manifestPath);
            return result;
        }

        var contractOps = new Dictionary<string, (string Description, List<(string Name, string Type)> Params, string ReturnType, List<string> SideEffects, string ErrorMode)>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < operationsArray.Count; i++)
        {
            if (operationsArray[i] is not JsonObject opObj)
            {
                result.AddError("MALFORMED_OPERATION", $"Operation item at index {i} must be an object.", manifestPath);
                continue;
            }

            string opName = opObj["name"]?.ToString() ?? "";
            string opDesc = opObj["description"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(opName))
            {
                result.AddError("MISSING_OPERATION_NAME", $"Operation at index {i} requires a non-empty 'name'.", manifestPath);
                continue;
            }
            if (string.IsNullOrWhiteSpace(opDesc))
            {
                result.AddError("MISSING_OPERATION_DESCRIPTION", $"Operation '{opName}' requires a non-empty 'description'.", manifestPath);
            }

            // Parameters
            var paramList = new List<(string Name, string Type)>();
            if (opObj["parameters"] is JsonArray pArray)
            {
                for (int pi = 0; pi < pArray.Count; pi++)
                {
                    if (pArray[pi] is not JsonObject pObj)
                    {
                        result.AddError("MALFORMED_PARAMETER", $"Parameter {pi} in operation '{opName}' must be an object.", manifestPath);
                        continue;
                    }
                    string pName = pObj["name"]?.ToString() ?? "";
                    string pType = pObj["type"]?.ToString() ?? "";

                    if (string.IsNullOrWhiteSpace(pName))
                    {
                        result.AddError("MISSING_PARAMETER_NAME", $"Parameter at index {pi} in operation '{opName}' requires a 'name'.", manifestPath);
                    }
                    if (string.IsNullOrWhiteSpace(pType) || !AllowedParamTypes.Contains(pType))
                    {
                        result.AddError("INVALID_PARAM_TYPE", $"Unknown parameter type '{pType}' for parameter '{pName}' in operation '{opName}'. Allowed types: {string.Join(", ", AllowedParamTypes)}.", manifestPath);
                    }
                    paramList.Add((pName, pType));
                }
            }
            else
            {
                result.AddError("MISSING_PARAMETERS", $"Operation '{opName}' requires a 'parameters' array.", manifestPath);
            }

            // Return Type
            string retType = "";
            if (opObj["returns"] is JsonObject retObj)
            {
                retType = retObj["type"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(retType) || !AllowedReturnTypes.Contains(retType))
                {
                    result.AddError("INVALID_RETURN_TYPE", $"Unknown return type '{retType}' in operation '{opName}'. Allowed types: {string.Join(", ", AllowedReturnTypes)}.", manifestPath);
                }
            }
            else
            {
                result.AddError("MISSING_RETURNS", $"Operation '{opName}' requires a 'returns' object with 'type'.", manifestPath);
            }

            // Side Effects
            var sideEffectsList = new List<string>();
            if (opObj["sideEffects"] is JsonArray seArray)
            {
                foreach (var seNode in seArray)
                {
                    string seVal = seNode?.ToString() ?? "";
                    if (!AllowedSideEffects.Contains(seVal))
                    {
                        result.AddError("INVALID_SIDE_EFFECT", $"Unknown side effect '{seVal}' in operation '{opName}'. Allowed: {string.Join(", ", AllowedSideEffects)}.", manifestPath);
                    }
                    else
                    {
                        sideEffectsList.Add(seVal);
                    }
                }
            }
            else
            {
                result.AddError("MISSING_SIDE_EFFECTS", $"Operation '{opName}' requires a 'sideEffects' array.", manifestPath);
            }

            // Error Mode
            string errorMode = opObj["errorMode"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(errorMode) || !AllowedErrorModes.Contains(errorMode))
            {
                result.AddError("INVALID_ERROR_MODE", $"Unknown errorMode '{errorMode}' in operation '{opName}'. Allowed: {string.Join(", ", AllowedErrorModes)}.", manifestPath);
            }

            // Misleading / contradictory contract declaration checks
            if (sideEffectsList.Contains("none") && sideEffectsList.Count > 1)
            {
                var activeEffects = sideEffectsList.Where(s => !string.Equals(s, "none", StringComparison.OrdinalIgnoreCase));
                result.AddError(
                    "MISLEADING_CONTRACT",
                    $"Operation '{opName}' has contradictory side-effect declarations: specifies 'none' alongside active side-effects ({string.Join(", ", activeEffects)}).",
                    manifestPath,
                    suggestion: "Remove 'none' if operation performs side effects, or remove active side effects."
                );
            }

            if (string.Equals(errorMode, "returns-false", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(retType, "boolean", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(retType, "any", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError(
                    "MISLEADING_CONTRACT",
                    $"Operation '{opName}' declares errorMode 'returns-false' but return type '{retType}'. Callers expecting {retType} will receive boolean false on failure, creating type dissonance.",
                    manifestPath,
                    suggestion: "Update return type to 'boolean' or 'any', or choose an errorMode consistent with the return type."
                );
            }

            if (string.Equals(retType, "void", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(errorMode, "returns-false", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(errorMode, "returns-fallback", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(errorMode, "returns-gone", StringComparison.OrdinalIgnoreCase)))
            {
                result.AddError(
                    "MISLEADING_CONTRACT",
                    $"Operation '{opName}' declares return type 'void' but errorMode '{errorMode}'. A void operation cannot return an error fallback value.",
                    manifestPath,
                    suggestion: "Change errorMode to 'throws-error' or declare an appropriate non-void return type."
                );
            }

            // Check duplicate parameter names
            var seenParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int pi = 0; pi < paramList.Count; pi++)
            {
                if (!seenParamNames.Add(paramList[pi].Name))
                {
                    result.AddError(
                        "DUPLICATE_PARAMETER_NAME",
                        $"Operation '{opName}' declares duplicate parameter name '{paramList[pi].Name}'.",
                        manifestPath,
                        suggestion: "Ensure each parameter name in the contract is unique."
                    );
                }
            }

            contractOps[opName] = (opDesc, paramList, retType, sideEffectsList, errorMode);
        }

        // 3. Dependencies Verification in requires.packages
        var declaredDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (manifest["requires"]?["packages"] is JsonObject reqPkgs)
        {
            foreach (var kvp in reqPkgs)
            {
                string depId = kvp.Key;
                if (!depId.Contains('/') || depId.Split('/').Length != 2)
                {
                    result.AddError("INVALID_DEPENDENCY_ID", $"Dependency '{depId}' in requires.packages must be formatted as 'publisher/name'.", manifestPath);
                }
                declaredDeps.Add(depId);
            }
        }
        if (manifest["dependencies"] is JsonObject legacyDeps)
        {
            foreach (var kvp in legacyDeps) declaredDeps.Add(kvp.Key);
        }

        // 4. Source Code and AST Cross-Check (Offline)
        string entryPath = Path.Combine(packDir, entry);
        if (!File.Exists(entryPath))
        {
            result.AddError("MISSING_ENTRY_FILE", $"Entry source file '{entry}' does not exist at '{entryPath}'.", entryPath);
            return result;
        }

        string sourceCode;
        try
        {
            sourceCode = File.ReadAllText(entryPath);
        }
        catch (Exception ex)
        {
            result.AddError("ENTRY_READ_ERROR", $"Failed to read entry file: {ex.Message}", entryPath);
            return result;
        }

        // Tokenize and Parse
        IReadOnlyList<Token> tokens;
        try
        {
            tokens = Lexer.Tokenize(sourceCode);
        }
        catch (Exception lexEx)
        {
            result.AddError("LEXER_ERROR", $"Tokenization error in '{entry}': {lexEx.Message}", entryPath);
            return result;
        }

        ProgramNode program;
        try
        {
            program = Parser.Parse(tokens);
        }
        catch (OtterMultipleErrors mult)
        {
            foreach (var err in mult.Diagnostics)
            {
                result.AddError("SYNTAX_ERROR", $"Syntax error in '{entry}': {err.Message}", entryPath, err.Line);
            }
            return result;
        }
        catch (OtterError err)
        {
            result.AddError("SYNTAX_ERROR", $"Syntax error in '{entry}': {err.Message}", entryPath, err.Line);
            return result;
        }

        // Extract functions from AST
        var astFunctions = new Dictionary<string, FunctionDefStmt>(StringComparer.OrdinalIgnoreCase);
        foreach (var stmt in program.Statements)
        {
            if (stmt is FunctionDefStmt fn)
            {
                astFunctions[fn.Name] = fn;
            }
        }

        // Verify that every declared contract operation actually exists in AST
        foreach (var kvp in contractOps)
        {
            string opName = kvp.Key;
            var opContract = kvp.Value;

            if (!astFunctions.TryGetValue(opName, out var fn))
            {
                result.AddError(
                    "MISSING_FUNCTION",
                    $"Declared operation '{opName}' is not defined as an exported function in '{entry}'.",
                    entryPath,
                    suggestion: $"Define function 'to {opName} ...' in {entry}"
                );
                continue;
            }

            int declaredParamCount = opContract.Params.Count;
            int actualParamCount = fn.Parameters.Count;
            if (declaredParamCount != actualParamCount)
            {
                string declaredNames = string.Join(", ", opContract.Params.Select(p => p.Name));
                string actualNames = string.Join(", ", fn.Parameters);
                result.AddError(
                    "PARAMETER_COUNT_MISMATCH",
                    $"Operation '{opName}' contract specifies {declaredParamCount} parameter(s) ({declaredNames}), but implementation defines {actualParamCount} parameter(s) ({actualNames}).",
                    entryPath,
                    fn.Line,
                    suggestion: $"Update parameter list in '{entry}' or contract in otter.pack.json"
                );
            }
            else
            {
                bool namesMatch = true;
                for (int pi = 0; pi < declaredParamCount; pi++)
                {
                    string declaredName = opContract.Params[pi].Name;
                    string actualName = fn.Parameters[pi];
                    if (!string.Equals(declaredName, actualName, StringComparison.OrdinalIgnoreCase))
                    {
                        namesMatch = false;
                        result.AddError(
                            "PARAMETER_NAME_MISMATCH",
                            $"Operation '{opName}' parameter {pi + 1} is declared as '{declaredName}' in contract, but AST function defines '{actualName}' in '{entry}'.",
                            entryPath,
                            fn.Line,
                            suggestion: $"Rename parameter in '{entry}' to '{declaredName}' or update otter.pack.json"
                        );
                    }
                }

                if (namesMatch)
                {
                    result.ValidatedOperations.Add(opName);
                }
            }
        }

        // Check for undeclared dependencies in source text
        string[] sourceLines = sourceCode.Replace("\r\n", "\n").Split('\n');
        for (int lineIdx = 0; lineIdx < sourceLines.Length; lineIdx++)
        {
            var lineMatch = UseLineRegex.Match(sourceLines[lineIdx]);
            if (lineMatch.Success)
            {
                string imported = lineMatch.Groups[1].Value.Trim();
                if (imported.Contains('/') || !imported.EndsWith(".ot", StringComparison.OrdinalIgnoreCase))
                {
                    if (!declaredDeps.Contains(imported))
                    {
                        result.AddError(
                            "UNDECLARED_DEPENDENCY",
                            $"Source imports package '{imported}' via 'use' at line {lineIdx + 1}, but it is not declared in manifest requires.packages.",
                            entryPath,
                            lineIdx + 1,
                            suggestion: $"Add \"{imported}\" to 'requires.packages' in otter.pack.json"
                        );
                    }
                }
            }
        }

        // Check variable prefixes for collision-risk mitigation (Requirement 9)
        if (!string.IsNullOrEmpty(result.VariablePrefix))
        {
            string prefix = result.VariablePrefix;
            foreach (var fn in astFunctions.Values)
            {
                var assignedVars = new List<(string Name, int Line)>();
                foreach (var stmt in fn.Body)
                {
                    CollectAssignedVariables(stmt, assignedVars);
                }

                var paramNames = new HashSet<string>(fn.Parameters, StringComparer.OrdinalIgnoreCase);
                foreach (var (varName, varLine) in assignedVars)
                {
                    if (paramNames.Contains(varName)) continue;
                    if (!varName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        result.AddError(
                            "VARIABLE_COLLISION_RISK",
                            $"Local variable '{varName}' at line {varLine} in function '{fn.Name}' does not use declared variablePrefix '{prefix}'. Under Otter 1.0.1 dynamic scoping, un-prefixed variables risk corrupting caller variables.",
                            entryPath,
                            varLine,
                            suggestion: $"Rename '{varName}' to '{prefix}{varName}'"
                        );
                    }
                }
            }
        }

        return result;
    }

    private static void CollectAssignedVariables(Stmt stmt, List<(string Name, int Line)> assigned)
    {
        switch (stmt)
        {
            case AssignStmt assign:
                if (assign.Target is VariableExpr v)
                {
                    assigned.Add((v.Name, stmt.Line));
                }
                break;
            case ObjectDefStmt objDef:
                assigned.Add((objDef.Name, stmt.Line));
                foreach (var prop in objDef.Properties)
                {
                    CollectAssignedVariables(prop, assigned);
                }
                break;
            case ListDefStmt listDef:
                assigned.Add((listDef.Name, stmt.Line));
                break;
            case MathIntoStmt mathInto:
                assigned.Add((mathInto.Target, stmt.Line));
                break;
            case AddToStmt addTo:
                assigned.Add((addTo.Target, stmt.Line));
                break;
            case RemoveFromStmt remFrom:
                assigned.Add((remFrom.Target, stmt.Line));
                break;
            case AskStmt ask:
                assigned.Add((ask.Name, stmt.Line));
                break;
            case CountStmt count:
                assigned.Add((count.VariableName, stmt.Line));
                foreach (var s in count.Body) CollectAssignedVariables(s, assigned);
                break;
            case ForEachStmt forEach:
                assigned.Add((forEach.VariableName, stmt.Line));
                foreach (var s in forEach.Body) CollectAssignedVariables(s, assigned);
                break;
            case IfStmt ifStmt:
                foreach (var branch in ifStmt.Branches)
                {
                    foreach (var s in branch.Body) CollectAssignedVariables(s, assigned);
                }
                if (ifStmt.ElseBody != null)
                {
                    foreach (var s in ifStmt.ElseBody) CollectAssignedVariables(s, assigned);
                }
                break;
            case WhileStmt whileStmt:
                foreach (var s in whileStmt.Body) CollectAssignedVariables(s, assigned);
                break;
            case RepeatStmt repeatStmt:
                foreach (var s in repeatStmt.Body) CollectAssignedVariables(s, assigned);
                break;
            case TryStmt tryStmt:
                if (!string.IsNullOrEmpty(tryStmt.ErrorTarget))
                {
                    assigned.Add((tryStmt.ErrorTarget, stmt.Line));
                }
                foreach (var s in tryStmt.Body) CollectAssignedVariables(s, assigned);
                if (tryStmt.OtherwiseBody != null)
                {
                    foreach (var s in tryStmt.OtherwiseBody) CollectAssignedVariables(s, assigned);
                }
                break;
            case CallStmt call:
                if (!string.IsNullOrEmpty(call.ResultTarget))
                {
                    assigned.Add((call.ResultTarget, stmt.Line));
                }
                break;
            case ReplaceStmt replace:
                if (!string.IsNullOrEmpty(replace.ResultTarget))
                {
                    assigned.Add((replace.ResultTarget, stmt.Line));
                }
                break;
            case SplitStmt split:
                if (!string.IsNullOrEmpty(split.Target))
                {
                    assigned.Add((split.Target, stmt.Line));
                }
                break;
            case JoinStmt join:
                if (!string.IsNullOrEmpty(join.Target))
                {
                    assigned.Add((join.Target, stmt.Line));
                }
                break;
            case FindStmt find:
                if (!string.IsNullOrEmpty(find.Target))
                {
                    assigned.Add((find.Target, stmt.Line));
                }
                break;
            case GetKeyStmt getKey:
                if (!string.IsNullOrEmpty(getKey.ResultTarget))
                {
                    assigned.Add((getKey.ResultTarget, stmt.Line));
                }
                break;
            case SortStmt sort:
                if (!string.IsNullOrEmpty(sort.Target))
                {
                    assigned.Add((sort.Target, stmt.Line));
                }
                break;
            case ReverseStmt rev:
                if (!string.IsNullOrEmpty(rev.Target))
                {
                    assigned.Add((rev.Target, stmt.Line));
                }
                break;
        }
    }
}
