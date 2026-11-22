using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Otter.Language;

namespace Otter.Cli;

/// <summary>
/// Phase 11 Milestone 2: Deterministic Multi-Pack Offline Composition Planner.
/// Universal, contract-driven composition engine. Derives operation selection, port bindings,
/// type validation, cycle detection, and code synthesis from machine-readable contracts and
/// declarative workflow specifications without hard-coded Pack names or operations.
/// </summary>
public static class CompositionPlanner
{
    // =========================================================================
    // 1. Component Contract Data Models
    // =========================================================================

    public sealed class PackContractInfo
    {
        public string Id { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Kind { get; set; } = "pack";
        public string Description { get; set; } = "";
        public string Entry { get; set; } = "src/main.ot";
        public string RequiresOtter { get; set; } = ">=1.0.1";
        public List<string> Platforms { get; set; } = new();
        public string VariablePrefix { get; set; } = "";
        public List<string> Capabilities { get; set; } = new();
        public List<OperationContractInfo> Operations { get; set; } = new();
        public Dictionary<string, string> RequiredPackages { get; set; } = new();
        public string ManifestPath { get; set; } = "";
    }

    public sealed class OperationContractInfo
    {
        public string PackId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<ParameterContractInfo> Parameters { get; set; } = new();
        public ReturnContractInfo Returns { get; set; } = new();
        public List<string> SideEffects { get; set; } = new();
        public string ErrorMode { get; set; } = "returns-fallback";
    }

    public sealed class ParameterContractInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "any";
        public string? ObjectType { get; set; }
        public bool Optional { get; set; }
        public object? DefaultValue { get; set; }
    }

    public sealed class ReturnContractInfo
    {
        public string Type { get; set; } = "any";
        public string? ObjectType { get; set; }
        public bool Nullable { get; set; }
    }

    public sealed class ComponentCatalog
    {
        public Dictionary<string, PackContractInfo> Packs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<WorkflowSpec> RegisteredWorkflows { get; } = new();

        public static ComponentCatalog Load(string libraryDir)
        {
            var catalog = new ComponentCatalog();
            string packsDir = Path.Combine(libraryDir, "packs");
            if (Directory.Exists(packsDir))
            {
                foreach (var pubDir in Directory.GetDirectories(packsDir))
                {
                    string pub = Path.GetFileName(pubDir);
                    foreach (var packDir in Directory.GetDirectories(pubDir))
                    {
                        string name = Path.GetFileName(packDir);
                        string manifestPath = Path.Combine(packDir, "otter.pack.json");
                        if (!File.Exists(manifestPath)) manifestPath = Path.Combine(packDir, "pack.json");
                        if (!File.Exists(manifestPath)) continue;

                        try
                        {
                            var pack = LoadPackFromManifest(manifestPath, pub, name);
                            if (pack != null) catalog.Packs[pack.Id] = pack;
                        }
                        catch
                        {
                            // Ignore malformed manifests during discovery
                        }
                    }
                }
            }

            // Load registered workflow fixtures
            string workflowsDir = Path.Combine(libraryDir, "workflows");
            if (Directory.Exists(workflowsDir))
            {
                foreach (var wfFile in Directory.GetFiles(workflowsDir, "*.json"))
                {
                    try
                    {
                        var wf = WorkflowSpec.Load(wfFile);
                        catalog.RegisteredWorkflows.Add(wf);
                    }
                    catch
                    {
                        // Ignore unparseable workflow fixtures
                    }
                }
            }

            return catalog;
        }

        public static PackContractInfo? LoadPackFromManifest(string manifestPath, string defaultPub = "", string defaultName = "")
        {
            var node = JsonNode.Parse(File.ReadAllText(manifestPath));
            if (node == null) return null;

            string pub = node["publisher"]?.ToString() ?? defaultPub;
            string name = node["name"]?.ToString() ?? defaultName;
            var pack = new PackContractInfo
            {
                Id = $"{pub}/{name}",
                Publisher = pub,
                Name = name,
                Version = node["version"]?.ToString() ?? "1.0.0",
                Kind = node["kind"]?.ToString() ?? "pack",
                Description = node["description"]?.ToString() ?? "",
                Entry = node["entry"]?.ToString() ?? "src/main.ot",
                RequiresOtter = node["requiresOtter"]?.ToString() ?? node["otter"]?.ToString() ?? ">=1.0.1",
                VariablePrefix = node["variablePrefix"]?.ToString() ?? "",
                ManifestPath = manifestPath
            };

            var plats = node["platforms"]?.AsArray();
            if (plats != null)
            {
                foreach (var p in plats) if (p != null) pack.Platforms.Add(p.ToString());
            }
            if (pack.Platforms.Count == 0) pack.Platforms.AddRange(new[] { "console", "desktop" });

            // Schema v2 provides
            var providesNode = node["provides"]?.AsObject();
            if (providesNode != null)
            {
                var caps = providesNode["capabilities"]?.AsArray();
                if (caps != null)
                {
                    foreach (var c in caps) if (c != null) pack.Capabilities.Add(c.ToString());
                }

                var ops = providesNode["operations"]?.AsArray();
                if (ops != null)
                {
                    foreach (var opNode in ops)
                    {
                        if (opNode == null) continue;
                        var op = new OperationContractInfo
                        {
                            PackId = pack.Id,
                            Name = opNode["name"]?.ToString() ?? "",
                            Description = opNode["description"]?.ToString() ?? "",
                            ErrorMode = opNode["errorMode"]?.ToString() ?? "returns-fallback"
                        };

                        var retNode = opNode["returns"];
                        if (retNode != null)
                        {
                            op.Returns = new ReturnContractInfo
                            {
                                Type = retNode["type"]?.ToString() ?? "any",
                                ObjectType = retNode["objectType"]?.ToString(),
                                Nullable = retNode["nullable"]?.GetValue<bool>() ?? false
                            };
                        }

                        var paramsNode = opNode["parameters"]?.AsArray();
                        if (paramsNode != null)
                        {
                            foreach (var p in paramsNode)
                            {
                                if (p == null) continue;
                                op.Parameters.Add(new ParameterContractInfo
                                {
                                    Name = p["name"]?.ToString() ?? "",
                                    Type = p["type"]?.ToString() ?? "any",
                                    ObjectType = p["objectType"]?.ToString(),
                                    Optional = p["optional"]?.GetValue<bool>() ?? false
                                });
                            }
                        }

                        var effects = opNode["sideEffects"]?.AsArray();
                        if (effects != null)
                        {
                            foreach (var eff in effects) if (eff != null) op.SideEffects.Add(eff.ToString());
                        }

                        pack.Operations.Add(op);
                    }
                }
            }
            else
            {
                // Fallback for Schema v1 legacy tags
                var v1Caps = node["capabilities"]?.AsArray();
                if (v1Caps != null)
                {
                    foreach (var c in v1Caps) if (c != null) pack.Capabilities.Add(c.ToString());
                }
            }

            // Requires
            var reqNode = node["requires"]?.AsObject();
            if (reqNode != null)
            {
                var pkgs = reqNode["packages"]?.AsObject();
                if (pkgs != null)
                {
                    foreach (var kv in pkgs)
                    {
                        if (kv.Value != null) pack.RequiredPackages[kv.Key] = kv.Value.ToString();
                    }
                }
            }

            return pack;
        }
    }

    // =========================================================================
    // 2. Declarative Workflow Specification Models
    // =========================================================================

    public sealed class WorkflowSpec
    {
        public int WorkflowVersion { get; set; } = 1;
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Target { get; set; } = "console";
        public List<string> ComposedPacks { get; set; } = new();
        public List<PipelineInputSpec> PipelineInputs { get; set; } = new();
        public List<WorkflowStepSpec> Steps { get; set; } = new();
        public WorkflowOutputSpec? Output { get; set; }

        public static WorkflowSpec Load(string path)
        {
            string json = File.ReadAllText(path);
            return Parse(json);
        }

        public static WorkflowSpec Parse(string json)
        {
            var node = JsonNode.Parse(json);
            if (node == null) throw new InvalidOperationException("Workflow JSON is empty or invalid.");
            var spec = new WorkflowSpec
            {
                WorkflowVersion = node["workflowVersion"]?.GetValue<int>() ?? 1,
                Id = node["id"]?.ToString() ?? "",
                Name = node["name"]?.ToString() ?? "",
                Description = node["description"]?.ToString() ?? "",
                Target = node["target"]?.ToString() ?? "console"
            };

            var packs = node["composedPacks"]?.AsArray();
            if (packs != null)
            {
                foreach (var p in packs) if (p != null) spec.ComposedPacks.Add(p.ToString());
            }

            var pinputs = node["pipelineInputs"]?.AsArray();
            if (pinputs != null)
            {
                foreach (var pi in pinputs)
                {
                    if (pi == null) continue;
                    spec.PipelineInputs.Add(new PipelineInputSpec
                    {
                        Name = pi["name"]?.ToString() ?? "",
                        Type = pi["type"]?.ToString() ?? "text",
                        DefaultValue = pi["default"],
                        Description = pi["description"]?.ToString() ?? ""
                    });
                }
            }

            var steps = node["steps"]?.AsArray();
            if (steps != null)
            {
                foreach (var s in steps)
                {
                    if (s == null) continue;
                    var step = new WorkflowStepSpec
                    {
                        Id = s["id"]?.ToString() ?? "",
                        Pack = s["pack"]?.ToString() ?? "",
                        Operation = s["operation"]?.ToString() ?? ""
                    };

                    var inputs = s["inputs"]?.AsObject();
                    if (inputs != null)
                    {
                        foreach (var kv in inputs)
                        {
                            if (kv.Value == null) continue;
                            step.Inputs[kv.Key] = new StepInputBinding
                            {
                                FromInput = kv.Value["fromInput"]?.ToString(),
                                FromStep = kv.Value["fromStep"]?.ToString(),
                                Port = kv.Value["port"]?.ToString() ?? "returns",
                                Value = kv.Value["value"],
                                Adapter = kv.Value["adapter"]?.ToString()
                            };
                        }
                    }

                    var order = s["orderAfter"]?.AsArray() ?? s["after"]?.AsArray();
                    if (order != null)
                    {
                        foreach (var o in order) if (o != null) step.OrderAfter.Add(o.ToString());
                    }

                    spec.Steps.Add(step);
                }
            }

            var outNode = node["output"];
            if (outNode != null)
            {
                spec.Output = new WorkflowOutputSpec
                {
                    FromStep = outNode["fromStep"]?.ToString() ?? "",
                    Port = outNode["port"]?.ToString() ?? "returns"
                };
            }

            return spec;
        }
    }

    public sealed class PipelineInputSpec
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "text";
        public JsonNode? DefaultValue { get; set; }
        public string Description { get; set; } = "";
    }

    public sealed class WorkflowStepSpec
    {
        public string Id { get; set; } = "";
        public string Pack { get; set; } = "";
        public string Operation { get; set; } = "";
        public Dictionary<string, StepInputBinding> Inputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> OrderAfter { get; set; } = new();
    }

    public sealed class StepInputBinding
    {
        public string? FromInput { get; set; }
        public string? FromStep { get; set; }
        public string Port { get; set; } = "returns";
        public JsonNode? Value { get; set; }
        public string? Adapter { get; set; }
    }

    public sealed class WorkflowOutputSpec
    {
        public string FromStep { get; set; } = "";
        public string Port { get; set; } = "returns";
    }

    // =========================================================================
    // 3. Real Adapter Registry & Contracts
    // =========================================================================

    public sealed class AdapterContract
    {
        public string Id { get; set; } = "";
        public string FromType { get; set; } = "";
        public string ToType { get; set; } = "";
        public string Description { get; set; } = "";
        public Func<string, string, List<string>> GenerateStatements { get; set; } = (_, _) => new();
    }

    public static class AdapterRegistry
    {
        private static readonly Dictionary<string, AdapterContract> _adapters = new(StringComparer.OrdinalIgnoreCase);

        static AdapterRegistry()
        {
            Register(new AdapterContract
            {
                Id = "number_to_text",
                FromType = "number",
                ToType = "text",
                Description = "Converts a numeric value to text representation using native 'convert to json'.",
                GenerateStatements = (src, tgt) => new() { $"convert {src} to json into {tgt}" }
            });

            Register(new AdapterContract
            {
                Id = "boolean_to_text",
                FromType = "boolean",
                ToType = "text",
                Description = "Converts a boolean value to text representation using native 'convert to json'.",
                GenerateStatements = (src, tgt) => new() { $"convert {src} to json into {tgt}" }
            });

            Register(new AdapterContract
            {
                Id = "list_to_text",
                FromType = "list",
                ToType = "text",
                Description = "Serializes a list value to JSON text using native 'convert to json'.",
                GenerateStatements = (src, tgt) => new() { $"convert {src} to json into {tgt}" }
            });

            Register(new AdapterContract
            {
                Id = "text_to_json_object",
                FromType = "text",
                ToType = "thing",
                Description = "Parses JSON text into an Otter thing object using native 'convert from json'.",
                GenerateStatements = (src, tgt) => new() { $"convert {src} from json into {tgt}" }
            });

            Register(new AdapterContract
            {
                Id = "json_object_to_text",
                FromType = "thing",
                ToType = "text",
                Description = "Serializes an Otter thing object to JSON text using native 'convert to json'.",
                GenerateStatements = (src, tgt) => new() { $"convert {src} to json into {tgt}" }
            });

            Register(new AdapterContract
            {
                Id = "json_object_wrap_slug",
                FromType = "text",
                ToType = "thing",
                Description = "Wraps a slug or text into a structured JSON thing record.",
                GenerateStatements = (src, tgt) => new()
                {
                    $"{tgt} is a thing",
                    $"slug of {tgt} is {src}"
                }
            });

            Register(new AdapterContract
            {
                Id = "json_object_wrap_value",
                FromType = "any",
                ToType = "thing",
                Description = "Wraps an arbitrary value into a structured thing with a 'value' property.",
                GenerateStatements = (src, tgt) => new()
                {
                    $"{tgt} is a thing",
                    $"value of {tgt} is {src}"
                }
            });
        }

        public static void Register(AdapterContract adapter) => _adapters[adapter.Id] = adapter;

        public static AdapterContract? Find(string id) =>
            _adapters.TryGetValue(id, out var a) ? a : null;

        public static AdapterContract? FindCompatible(string fromType, string toType)
        {
            string f = fromType.ToLowerInvariant();
            string t = toType.ToLowerInvariant();
            return _adapters.Values.FirstOrDefault(a =>
                (a.FromType.Equals(f, StringComparison.OrdinalIgnoreCase) || a.FromType == "any") &&
                (a.ToType.Equals(t, StringComparison.OrdinalIgnoreCase) || a.ToType == "any"));
        }

        public static IEnumerable<AdapterContract> All => _adapters.Values;
    }

    // =========================================================================
    // 4. Graph & Planning Data Models
    // =========================================================================

    public enum EdgeKind
    {
        Data,
        Ordering
    }

    public sealed class GraphNode
    {
        public string Id { get; set; } = "";
        public string PackId { get; set; } = "";
        public string OperationName { get; set; } = "";
        public OperationContractInfo Operation { get; set; } = new();
        public string ResultVar { get; set; } = "";
        public Dictionary<string, StepInputBinding> ParameterBindings { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class GraphEdge
    {
        public EdgeKind Kind { get; set; } = EdgeKind.Data;
        public string FromNodeId { get; set; } = "";
        public string FromPort { get; set; } = "returns";
        public string FromType { get; set; } = "any";
        public string ToNodeId { get; set; } = "";
        public string ToPort { get; set; } = "";
        public string ToType { get; set; } = "any";
        public bool Compatible { get; set; } = true;
        public string? AdapterId { get; set; }
        public AdapterContract? Adapter { get; set; }
        public string? Error { get; set; }
    }

    public sealed class CompositionGraph
    {
        public List<GraphNode> Nodes { get; } = new();
        public List<GraphEdge> Edges { get; } = new();
        public List<string> TopologicalOrder { get; } = new();
        public List<CoverageItem> CapabilityCoverage { get; } = new();
        public List<PipelineInputSpec> PipelineInputs { get; } = new();
        public WorkflowOutputSpec? PipelineOutput { get; set; }
        public List<string> Diagnostics { get; } = new();
        public bool IsValid => Diagnostics.Count == 0;
    }

    public sealed class CoverageItem
    {
        public string Capability { get; set; } = "";
        public string Status { get; set; } = "implemented";
        public string? FulfilledBy { get; set; }
        public string Rationale { get; set; } = "";

        public CoverageItem(string cap, string status, string? fulfilledBy, string rationale)
        {
            Capability = cap;
            Status = status;
            FulfilledBy = fulfilledBy;
            Rationale = rationale;
        }
    }

    public sealed class PlanRequest
    {
        public string Intent { get; set; } = "";
        public List<string> ExplicitPacks { get; set; } = new();
        public string TargetRuntime { get; set; } = "console";
        public string OutputPath { get; set; } = "src/main.ot";
        public string? WorkflowSpecPath { get; set; }
    }

    public sealed class PlanResult
    {
        public bool Success { get; set; }
        public string ProposalId { get; set; } = "";
        public JsonObject ProposalJson { get; set; } = new();
        public CompositionGraph Graph { get; set; } = new();
        public string GeneratedCode { get; set; } = "";
        public List<string> Diagnostics { get; set; } = new();
    }

    // =========================================================================
    // 5. Core Composition Planner Engine
    // =========================================================================

    public static PlanResult Plan(PlanRequest request, string libraryDir)
    {
        var catalog = ComponentCatalog.Load(libraryDir);
        var result = new PlanResult
        {
            ProposalId = $"prop-{RandomNumberGenerator.GetHexString(8).ToLowerInvariant()}"
        };

        WorkflowSpec? workflow = null;

        // Step 1: Resolve Workflow Specification
        if (!string.IsNullOrEmpty(request.WorkflowSpecPath))
        {
            if (!File.Exists(request.WorkflowSpecPath))
            {
                result.Success = false;
                result.Diagnostics.Add($"[WORKFLOW_NOT_FOUND] Workflow specification file not found: '{request.WorkflowSpecPath}'.");
                return result;
            }

            try
            {
                workflow = WorkflowSpec.Load(request.WorkflowSpecPath);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Diagnostics.Add($"[MALFORMED_WORKFLOW] Failed to parse workflow specification: {ex.Message}");
                return result;
            }
        }
        else if (request.ExplicitPacks.Count > 0)
        {
            // Check if any registered workflow fixture matches the requested packs
            workflow = catalog.RegisteredWorkflows.FirstOrDefault(w =>
                w.ComposedPacks.Count > 0 &&
                w.ComposedPacks.Count == request.ExplicitPacks.Count &&
                w.ComposedPacks.All(p => request.ExplicitPacks.Contains(p, StringComparer.OrdinalIgnoreCase)));
        }

        // Step 2: Resolve Participating Packs
        var candidatePacks = new List<PackContractInfo>();
        var packIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (workflow != null)
        {
            foreach (var step in workflow.Steps)
            {
                if (!string.IsNullOrWhiteSpace(step.Pack)) packIds.Add(step.Pack);
            }
            foreach (var p in workflow.ComposedPacks) packIds.Add(p);
        }
        else if (request.ExplicitPacks.Count > 0)
        {
            foreach (var p in request.ExplicitPacks) packIds.Add(p);
        }
        else if (!string.IsNullOrWhiteSpace(request.Intent))
        {
            var discovery = MatchIntentCapabilities(request.Intent, catalog);
            if (!discovery.Supported)
            {
                result.Success = false;
                result.Diagnostics.AddRange(discovery.Diagnostics);
                result.ProposalJson = CreateUnsupportedProposal(result.ProposalId, request.Intent, discovery);
                return result;
            }

            foreach (var p in discovery.SelectedPacks)
            {
                candidatePacks.Add(p);
                packIds.Add(p.Id);
            }
            foreach (var cov in discovery.CoverageMatrix)
            {
                result.Graph.CapabilityCoverage.Add(cov);
            }
        }

        foreach (var pid in packIds)
        {
            if (catalog.Packs.TryGetValue(pid, out var pack))
            {
                if (!candidatePacks.Any(p => p.Id.Equals(pid, StringComparison.OrdinalIgnoreCase)))
                {
                    candidatePacks.Add(pack);
                }
            }
            else
            {
                result.Diagnostics.Add($"[MISSING_PACK] Component pack '{pid}' not found in offline catalog.");
            }
        }

        if (workflow == null && candidatePacks.Count > 0)
        {
            workflow = catalog.RegisteredWorkflows.FirstOrDefault(w =>
                w.ComposedPacks.Count > 0 &&
                w.ComposedPacks.Count == candidatePacks.Count &&
                w.ComposedPacks.All(p => candidatePacks.Any(cp => cp.Id.Equals(p, StringComparison.OrdinalIgnoreCase))));
        }

        if (candidatePacks.Count == 0 && result.Diagnostics.Count == 0)
        {
            result.Success = false;
            result.Diagnostics.Add("[NO_PACKS] No compatible packs could be matched for composition.");
            return result;
        }

        // Step 3: Validate Runtime, Platforms & Scoping Invariants
        string targetRuntime = (workflow != null && !string.IsNullOrEmpty(workflow.Target) ? workflow.Target : request.TargetRuntime).ToLowerInvariant();
        var allPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < candidatePacks.Count; i++)
        {
            var pack = candidatePacks[i];
            if (!pack.Platforms.Contains(targetRuntime) && !pack.Platforms.Contains("all"))
            {
                result.Diagnostics.Add($"[UNSUPPORTED_PLATFORM] Pack '{pack.Id}' does not support target platform '{targetRuntime}'. Supported platforms: {string.Join(", ", pack.Platforms)}.");
            }

            if (pack.RequiresOtter.Contains("2.0.0") || pack.RequiresOtter.StartsWith(">1."))
            {
                result.Diagnostics.Add($"[INCOMPATIBLE_RUNTIME] Pack '{pack.Id}' requires Otter runtime '{pack.RequiresOtter}', but current runtime is 1.0.1.");
            }

            if (string.IsNullOrWhiteSpace(pack.VariablePrefix))
            {
                result.Diagnostics.Add($"[MISSING_VARIABLE_PREFIX] Pack '{pack.Id}' does not declare 'variablePrefix' contract, risking caller variable collisions in Otter 1.0.1.");
            }
            else if (pack.VariablePrefix.Equals("_orch_", StringComparison.OrdinalIgnoreCase))
            {
                result.Diagnostics.Add($"[PREFIX_COLLISION] Pack '{pack.Id}' uses reserved orchestrator prefix '_orch_'.");
            }
            else if (!allPrefixes.Add(pack.VariablePrefix))
            {
                result.Diagnostics.Add($"[PREFIX_COLLISION] Multiple components share variablePrefix '{pack.VariablePrefix}'. This causes caller-scope variable clashing under Otter 1.0.1 dynamic scoping.");
            }

            foreach (var (reqPkg, reqVer) in pack.RequiredPackages)
            {
                if (!candidatePacks.Any(p => p.Id.Equals(reqPkg, StringComparison.OrdinalIgnoreCase)))
                {
                    if (catalog.Packs.TryGetValue(reqPkg, out var depPack))
                    {
                        candidatePacks.Add(depPack);
                    }
                    else
                    {
                        result.Diagnostics.Add($"[MISSING_DEPENDENCY] Pack '{pack.Id}' requires package '{reqPkg}' ({reqVer}), which was not found in catalog.");
                    }
                }
            }
        }

        if (result.Diagnostics.Count > 0)
        {
            result.Success = false;
            return result;
        }

        // Step 4: Assemble Composition Graph from Workflow Specification or Semantic Inference
        if (workflow != null)
        {
            BuildWorkflowGraph(workflow, candidatePacks, result.Graph);
        }
        else
        {
            BuildInferredGraph(candidatePacks, result.Graph);
        }

        // Step 5: Detect Cycles & Determine Deterministic Execution Order
        DetectCyclesAndOrder(result.Graph);

        if (!result.Graph.IsValid)
        {
            result.Success = false;
            result.Diagnostics.AddRange(result.Graph.Diagnostics);
            return result;
        }

        // Step 6: Synthesize Idiomatic Otter 1.0.1 Orchestrator Source Code
        string synthesizedCode = SynthesizeOrchestratorCode(candidatePacks, result.Graph, workflow, request.OutputPath, targetRuntime);
        result.GeneratedCode = synthesizedCode;

        // Step 7: Validate Synthesized AST with LanguageService
        string codeForValidation = ComposeCommand.ResolveVirtualImports(synthesizedCode, libraryDir);
        var syntaxDiags = LanguageService.GetDiagnostics(codeForValidation, request.OutputPath);
        bool syntaxValid = syntaxDiags.Count == 0;
        var diagArray = new JsonArray();
        foreach (var d in syntaxDiags)
        {
            string msg = $"{request.OutputPath}:{d.Line}: {d.Message}";
            diagArray.Add((JsonNode)JsonValue.Create(msg)!);
            result.Diagnostics.Add($"[SYNTAX_ERROR] {msg}");
        }

        if (!syntaxValid)
        {
            result.Success = false;
            return result;
        }

        // Step 8: Build Proposal JSON Artifact
        result.ProposalJson = BuildProposalJson(result.ProposalId, request, candidatePacks, result.Graph, synthesizedCode, syntaxValid, diagArray);
        result.Success = true;
        return result;
    }

    // =========================================================================
    // 6. Workflow Graph Assembly & Semantic Port Validation
    // =========================================================================

    private static void BuildWorkflowGraph(WorkflowSpec workflow, List<PackContractInfo> packs, CompositionGraph graph)
    {
        var packMap = packs.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

        // Copy pipeline inputs & outputs
        foreach (var pi in workflow.PipelineInputs) graph.PipelineInputs.Add(pi);
        graph.PipelineOutput = workflow.Output;

        // 1. Create nodes and index them
        var nodeMap = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in workflow.Steps)
        {
            if (!packMap.TryGetValue(step.Pack, out var pack))
            {
                graph.Diagnostics.Add($"[MISSING_PACK] Step '{step.Id}' references undeclared pack '{step.Pack}'.");
                continue;
            }

            var op = pack.Operations.FirstOrDefault(o => o.Name.Equals(step.Operation, StringComparison.OrdinalIgnoreCase));
            if (op == null)
            {
                graph.Diagnostics.Add($"[MISSING_OPERATION] Pack '{pack.Id}' does not declare operation '{step.Operation}' in its component contract.");
                continue;
            }

            var node = new GraphNode
            {
                Id = step.Id,
                PackId = pack.Id,
                OperationName = op.Name,
                Operation = op,
                ResultVar = $"_orch_{step.Id}_res"
            };

            foreach (var kv in step.Inputs) node.ParameterBindings[kv.Key] = kv.Value;
            graph.Nodes.Add(node);
            nodeMap[step.Id] = node;
        }

        if (graph.Diagnostics.Count > 0) return;

        // 2. Validate parameter bindings & build Data edges
        foreach (var step in workflow.Steps)
        {
            if (!nodeMap.TryGetValue(step.Id, out var targetNode)) continue;
            var op = targetNode.Operation;

            // Check each parameter declared in the operation contract
            foreach (var param in op.Parameters)
            {
                if (step.Inputs.TryGetValue(param.Name, out var binding))
                {
                    if (!string.IsNullOrEmpty(binding.FromStep))
                    {
                        if (!nodeMap.TryGetValue(binding.FromStep, out var sourceNode))
                        {
                            graph.Diagnostics.Add($"[UNKNOWN_STEP] Step '{step.Id}' input '{param.Name}' references non-existent step '{binding.FromStep}'.");
                            continue;
                        }

                        string fromPort = string.IsNullOrEmpty(binding.Port) ? "returns" : binding.Port;
                        string fromType = sourceNode.Operation.Returns.Type;
                        string toType = param.Type;

                        // Type compatibility & adapter validation
                        bool directCompatible = IsTypeCompatible(fromType, toType);
                        AdapterContract? adapter = null;

                        if (!string.IsNullOrEmpty(binding.Adapter))
                        {
                            adapter = AdapterRegistry.Find(binding.Adapter);
                            if (adapter == null)
                            {
                                graph.Diagnostics.Add($"[INVALID_ADAPTER] Step '{step.Id}' references unregistered adapter '{binding.Adapter}'.");
                                continue;
                            }

                            if (!IsTypeCompatible(fromType, adapter.FromType) || !IsTypeCompatible(adapter.ToType, toType))
                            {
                                graph.Diagnostics.Add($"[ADAPTER_TYPE_MISMATCH] Adapter '{adapter.Id}' converts '{adapter.FromType}' -> '{adapter.ToType}', but port requires '{fromType}' -> '{toType}'.");
                                continue;
                            }
                        }
                        else if (!directCompatible)
                        {
                            adapter = AdapterRegistry.FindCompatible(fromType, toType);
                            if (adapter == null)
                            {
                                graph.Diagnostics.Add($"[TYPE_INCOMPATIBLE] Cannot bind output type '{fromType}' from step '{sourceNode.Id}' to parameter '{param.Name}' of type '{toType}' on step '{step.Id}'. No compatible adapter found.");
                                continue;
                            }
                        }

                        graph.Edges.Add(new GraphEdge
                        {
                            Kind = EdgeKind.Data,
                            FromNodeId = sourceNode.Id,
                            FromPort = fromPort,
                            FromType = fromType,
                            ToNodeId = targetNode.Id,
                            ToPort = param.Name,
                            ToType = toType,
                            Compatible = true,
                            AdapterId = adapter?.Id,
                            Adapter = adapter
                        });
                    }
                    else if (!string.IsNullOrEmpty(binding.FromInput))
                    {
                        var pipeIn = workflow.PipelineInputs.FirstOrDefault(pi => pi.Name.Equals(binding.FromInput, StringComparison.OrdinalIgnoreCase));
                        if (pipeIn == null)
                        {
                            graph.Diagnostics.Add($"[UNKNOWN_PIPELINE_INPUT] Step '{step.Id}' input '{param.Name}' references undeclared pipeline input '{binding.FromInput}'.");
                        }
                    }
                }
                else if (!param.Optional && param.DefaultValue == null)
                {
                    graph.Diagnostics.Add($"[MISSING_REQUIRED_INPUT] Step '{step.Id}' is missing binding for required parameter '{param.Name}' ({param.Type}) on operation '{op.Name}'.");
                }
            }

            // 3. Add explicit Ordering-only edges (orderAfter)
            foreach (var orderStepId in step.OrderAfter)
            {
                if (!nodeMap.TryGetValue(orderStepId, out var sourceNode))
                {
                    graph.Diagnostics.Add($"[UNKNOWN_STEP] Step '{step.Id}' declares ordering dependency on non-existent step '{orderStepId}'.");
                    continue;
                }

                // Avoid duplicate edge if data edge already exists between these nodes
                if (!graph.Edges.Any(e => e.FromNodeId == sourceNode.Id && e.ToNodeId == targetNode.Id))
                {
                    graph.Edges.Add(new GraphEdge
                    {
                        Kind = EdgeKind.Ordering,
                        FromNodeId = sourceNode.Id,
                        FromPort = "complete",
                        FromType = "void",
                        ToNodeId = targetNode.Id,
                        ToPort = "start",
                        ToType = "void",
                        Compatible = true
                    });
                }
            }
        }
    }

    private static void BuildInferredGraph(List<PackContractInfo> packs, CompositionGraph graph)
    {
        // When no workflow specification is provided, perform contract-driven semantic port inference.
        // If an operation input has multiple matching candidate outputs, report ambiguity instead of guessing.
        int step = 1;
        var availableOutputs = new List<(string StepId, string Port, string Type, string PackId, string OpName)>();

        foreach (var pack in packs)
        {
            foreach (var op in pack.Operations)
            {
                var node = new GraphNode
                {
                    Id = $"step{step++}_{op.Name}",
                    PackId = pack.Id,
                    OperationName = op.Name,
                    Operation = op,
                    ResultVar = $"_orch_{op.Name}_res"
                };

                foreach (var param in op.Parameters)
                {
                    // Find candidate outputs that match parameter type
                    var candidates = availableOutputs.Where(o => IsTypeCompatible(o.Type, param.Type)).ToList();

                    if (candidates.Count == 1)
                    {
                        var cand = candidates[0];
                        graph.Edges.Add(new GraphEdge
                        {
                            Kind = EdgeKind.Data,
                            FromNodeId = cand.StepId,
                            FromPort = cand.Port,
                            FromType = cand.Type,
                            ToNodeId = node.Id,
                            ToPort = param.Name,
                            ToType = param.Type,
                            Compatible = true
                        });
                    }
                    else if (candidates.Count > 1)
                    {
                        string candList = string.Join(", ", candidates.Select(c => $"[{c.PackId}]::{c.OpName}.{c.Port} ({c.Type})"));
                        graph.Diagnostics.Add($"[AMBIGUOUS_CONNECTION] Ambiguous connection for parameter '{param.Name}' on '[{pack.Id}]::{op.Name}'. Multiple candidate outputs [{candList}]. Provide an explicit --workflow specification to disambiguate.");
                    }
                }

                availableOutputs.Add((node.Id, "returns", op.Returns.Type, pack.Id, op.Name));
                graph.Nodes.Add(node);
            }
        }
    }

    private static bool IsTypeCompatible(string fromType, string toType)
    {
        string f = fromType.ToLowerInvariant();
        string t = toType.ToLowerInvariant();
        if (f == t || f == "any" || t == "any") return true;
        return false;
    }

    // =========================================================================
    // 7. Cycle Detection & Topological Sort (Kahn's Algorithm)
    // =========================================================================

    public static void DetectCyclesAndOrder(CompositionGraph graph)
    {
        var nodeIds = graph.Nodes.Select(n => n.Id).ToList();
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var adj = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in nodeIds)
        {
            inDegree[id] = 0;
            adj[id] = new List<string>();
        }

        foreach (var edge in graph.Edges)
        {
            if (adj.ContainsKey(edge.FromNodeId) && inDegree.ContainsKey(edge.ToNodeId))
            {
                adj[edge.FromNodeId].Add(edge.ToNodeId);
                inDegree[edge.ToNodeId]++;
            }
        }

        var queue = new Queue<string>(nodeIds.Where(id => inDegree[id] == 0));
        var order = new List<string>();

        while (queue.Count > 0)
        {
            string curr = queue.Dequeue();
            order.Add(curr);

            foreach (var neighbor in adj[curr])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        if (order.Count != nodeIds.Count)
        {
            var cycleNodes = nodeIds.Where(id => inDegree[id] > 0).ToList();
            string cyclePath = string.Join(" -> ", cycleNodes) + " -> " + cycleNodes.FirstOrDefault();
            graph.Diagnostics.Add($"[DEPENDENCY_CYCLE] Dependency cycle detected in component workflow graph: [{cyclePath}].");
            return;
        }

        graph.TopologicalOrder.AddRange(order);
    }

    // =========================================================================
    // 8. General-Purpose Code Synthesizer (Otter 1.0.1)
    // =========================================================================

    private static string SynthesizeOrchestratorCode(
        List<PackContractInfo> packs,
        CompositionGraph graph,
        WorkflowSpec? workflow,
        string outputPath,
        string targetRuntime)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# ===========================================================================");
        sb.AppendLine("# Generated by Otter Deterministic Composition Planner (Milestone 2)");
        sb.AppendLine($"# Target: {targetRuntime}");
        sb.AppendLine("# Composed Component Contracts:");
        foreach (var p in packs)
        {
            sb.AppendLine($"#   - {p.Id} v{p.Version} (prefix: {p.VariablePrefix}, capabilities: {string.Join(", ", p.Capabilities)})");
        }
        sb.AppendLine("# Dynamic Scoping Isolation: Synthesized orchestrator uses prefix '_orch_'");
        sb.AppendLine("# ===========================================================================");
        sb.AppendLine();

        // Distinct package imports using ${import:<id>} for transactional staging.
        // Emit transitive required packages before direct workflow packs so imported operations resolve cleanly.
        var directPacks = graph.Nodes.Select(n => n.PackId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var distinctPacks = packs
            .Select(p => p.Id)
            .Where(id => !directPacks.Contains(id, StringComparer.OrdinalIgnoreCase))
            .Concat(directPacks)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var pid in distinctPacks)
        {
            sb.AppendLine($"use \"${{import:{pid}}}\"");
        }
        sb.AppendLine();

        // Build pipeline function signature
        var pinputs = graph.PipelineInputs;
        string sigArgs = pinputs.Count > 0 ? " " + string.Join(" ", pinputs.Select(pi => pi.Name)) : "";
        sb.AppendLine($"to runCompositionPipeline{sigArgs}");

        // Emit steps in topological execution order
        for (int i = 0; i < graph.TopologicalOrder.Count; i++)
        {
            string stepId = graph.TopologicalOrder[i];
            var node = graph.Nodes.First(n => n.Id == stepId);
            sb.AppendLine($"    # Step {i + 1}: [{node.PackId}]::{node.OperationName} (step: {node.Id})");

            var callArgVars = new List<string>();

            foreach (var param in node.Operation.Parameters)
            {
                string argVar = $"_orch_{node.Id}_{param.Name}";
                callArgVars.Add(argVar);

                // Check for incoming data edge
                var dataEdge = graph.Edges.FirstOrDefault(e =>
                    e.Kind == EdgeKind.Data &&
                    e.ToNodeId == node.Id &&
                    e.ToPort.Equals(param.Name, StringComparison.OrdinalIgnoreCase));

                if (dataEdge != null)
                {
                    var srcNode = graph.Nodes.First(n => n.Id == dataEdge.FromNodeId);
                    if (dataEdge.Adapter != null)
                    {
                        var stmts = dataEdge.Adapter.GenerateStatements(srcNode.ResultVar, argVar);
                        foreach (var stmt in stmts)
                        {
                            sb.AppendLine($"    {stmt}");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"    {argVar} is {srcNode.ResultVar}");
                    }
                }
                else if (node.ParameterBindings.TryGetValue(param.Name, out var binding))
                {
                    if (!string.IsNullOrEmpty(binding.FromInput))
                    {
                        sb.AppendLine($"    {argVar} is {binding.FromInput}");
                    }
                    else if (binding.Value != null)
                    {
                        EmitValueAssignment(sb, "    ", argVar, binding.Value);
                    }
                    else
                    {
                        sb.AppendLine($"    {argVar} is gone");
                    }
                }
                else if (param.Optional)
                {
                    sb.AppendLine($"    {argVar} is gone");
                }
                else
                {
                    sb.AppendLine($"    {argVar} is gone");
                }
            }

            // Call imported operation using Otter 1.0.1 statement calling syntax
            if (callArgVars.Count > 0)
            {
                sb.AppendLine($"    {node.OperationName} {string.Join(" ", callArgVars)} into {node.ResultVar}");
            }
            else
            {
                sb.AppendLine($"    {node.OperationName} into {node.ResultVar}");
            }
            sb.AppendLine();
        }

        // Return desired output
        string returnVar = "_orch_final_result";
        if (graph.PipelineOutput != null && !string.IsNullOrEmpty(graph.PipelineOutput.FromStep))
        {
            var outNode = graph.Nodes.FirstOrDefault(n => n.Id == graph.PipelineOutput.FromStep);
            if (outNode != null) returnVar = outNode.ResultVar;
        }
        else if (graph.Nodes.Count > 0)
        {
            string lastStepId = graph.TopologicalOrder.Last();
            var lastNode = graph.Nodes.First(n => n.Id == lastStepId);
            returnVar = lastNode.ResultVar;
        }

        sb.AppendLine($"    return {returnVar}");
        sb.AppendLine(".");
        sb.AppendLine();

        // Standalone Test / Execution Harness
        sb.AppendLine("# Standalone Execution Harness");
        var harnessArgs = new List<string>();
        foreach (var pi in pinputs)
        {
            string inVar = $"_orch_in_{pi.Name}";
            harnessArgs.Add(inVar);
            if (pi.DefaultValue != null)
            {
                EmitValueAssignment(sb, "", inVar, pi.DefaultValue);
            }
            else
            {
                sb.AppendLine($"{inVar} is \"test\"");
            }
        }

        string harnessCall = harnessArgs.Count > 0
            ? $"runCompositionPipeline {string.Join(" ", harnessArgs)} into _orch_pipeline_res"
            : "runCompositionPipeline into _orch_pipeline_res";

        sb.AppendLine(harnessCall);
        sb.AppendLine("say \"Pipeline execution complete. Result: \"");
        sb.AppendLine("say _orch_pipeline_res");

        return sb.ToString();
    }

    private static void EmitValueAssignment(StringBuilder sb, string indent, string varName, JsonNode? node)
    {
        if (node is JsonArray arr)
        {
            if (arr.Count == 0)
            {
                sb.AppendLine($"{indent}{varName} are empty");
                return;
            }
            sb.AppendLine($"{indent}{varName} are");
            foreach (var item in arr)
            {
                sb.AppendLine($"{indent}    {FormatLiteralValue(item)}");
            }
            sb.AppendLine($"{indent}.");
            return;
        }
        sb.AppendLine($"{indent}{varName} is {FormatLiteralValue(node)}");
    }

    private static string FormatLiteralValue(JsonNode? node)
    {
        if (node == null) return "gone";
        if (node is JsonValue jval)
        {
            if (jval.TryGetValue<string>(out var s))
            {
                return $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
            }
            if (jval.TryGetValue<bool>(out var b))
            {
                return b ? "true" : "false";
            }
            if (jval.TryGetValue<double>(out var d))
            {
                return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        return "gone";
    }

    // =========================================================================
    // 9. Proposal JSON & Explanation Formatting
    // =========================================================================

    private static JsonObject BuildProposalJson(
        string proposalId,
        PlanRequest request,
        List<PackContractInfo> packs,
        CompositionGraph graph,
        string code,
        bool syntaxValid,
        JsonArray syntaxDiags)
    {
        var recommended = new JsonArray();
        foreach (var p in packs)
        {
            recommended.Add(new JsonObject
            {
                ["id"] = p.Id,
                ["kind"] = p.Kind,
                ["version"] = p.Version,
                ["rationale"] = $"Provides verified operation contracts: {string.Join(", ", p.Operations.Select(o => o.Name))}"
            });
        }

        var composedPacks = new JsonArray();
        foreach (var p in packs) composedPacks.Add((JsonNode)JsonValue.Create(p.Id)!);

        var conns = new JsonArray();
        foreach (var e in graph.Edges)
        {
            conns.Add(new JsonObject
            {
                ["source"] = $"{e.FromNodeId}.{e.FromPort}",
                ["destination"] = $"{e.ToNodeId}.{e.ToPort}",
                ["kind"] = e.Kind.ToString().ToLowerInvariant(),
                ["description"] = e.Kind == EdgeKind.Data
                    ? $"Binds '{e.FromType}' to '{e.ToType}'" + (e.Adapter != null ? $" via adapter {e.Adapter.Id}" : "")
                    : "Explicit ordering dependency"
            });
        }

        var graphJson = new JsonObject
        {
            ["nodes"] = new JsonArray(graph.Nodes.Select(n => (JsonNode)new JsonObject
            {
                ["id"] = n.Id,
                ["pack"] = n.PackId,
                ["operation"] = n.OperationName,
                ["resultVariable"] = n.ResultVar
            }).ToArray()),
            ["edges"] = new JsonArray(graph.Edges.Select(e => (JsonNode)new JsonObject
            {
                ["from"] = e.FromNodeId,
                ["to"] = e.ToNodeId,
                ["fromPort"] = e.FromPort,
                ["toPort"] = e.ToPort,
                ["fromType"] = e.FromType,
                ["toType"] = e.ToType,
                ["kind"] = e.Kind.ToString().ToLowerInvariant(),
                ["compatible"] = e.Compatible,
                ["adapter"] = e.Adapter?.Id
            }).ToArray()),
            ["topologicalOrder"] = new JsonArray(graph.TopologicalOrder.Select(o => (JsonNode)JsonValue.Create(o)!).ToArray())
        };

        var coverage = new JsonArray();
        foreach (var cov in graph.CapabilityCoverage)
        {
            coverage.Add(new JsonObject
            {
                ["capability"] = cov.Capability,
                ["status"] = cov.Status,
                ["fulfilledBy"] = cov.FulfilledBy,
                ["rationale"] = cov.Rationale
            });
        }

        var sourceFiles = new JsonArray
        {
            new JsonObject
            {
                ["path"] = request.OutputPath,
                ["sourceCode"] = code,
                ["sourceMappings"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["lines"] = "1-60",
                        ["componentId"] = string.Join("+", packs.Select(p => p.Id))
                    }
                }
            }
        };

        var limitations = new JsonArray
        {
            (JsonNode)JsonValue.Create("Otter 1.0.1 variable scoping: Pack functions execute in caller variable scope. Dynamic scoping collisions are prevented by contract-verified variablePrefix conventions and orchestrator isolation prefix '_orch_'.")!,
            (JsonNode)JsonValue.Create("Multi-file filesystem atomicity: Operating systems lack transactional multi-file atomic writes. Otter guarantees safety via two-phase staging and byte-verified compensating rollback.")!
        };

        return new JsonObject
        {
            ["schemaVersion"] = 2,
            ["proposalId"] = proposalId,
            ["intent"] = string.IsNullOrWhiteSpace(request.Intent) ? "Multi-Pack Composition" : request.Intent,
            ["requestedCapabilities"] = new JsonArray(graph.CapabilityCoverage.Select(c => (JsonNode)JsonValue.Create(c.Capability)!).ToArray()),
            ["capabilityCoverage"] = coverage,
            ["recommendedComponents"] = recommended,
            ["compositionPlan"] = new JsonObject
            {
                ["target"] = request.TargetRuntime,
                ["composedPacks"] = composedPacks,
                ["composedBlueprints"] = new JsonArray(),
                ["connections"] = conns,
                ["graph"] = graphJson
            },
            ["generatedSourcePreview"] = sourceFiles,
            ["approvalRequired"] = true,
            ["verification"] = new JsonObject
            {
                ["command"] = $"otter check {request.OutputPath}",
                ["syntaxValid"] = syntaxValid,
                ["diagnostics"] = syntaxDiags
            },
            ["unresolvedLimitations"] = limitations
        };
    }

    private static JsonObject CreateUnsupportedProposal(string proposalId, string intent, DiscoveryOutcome discovery)
    {
        var coverage = new JsonArray();
        foreach (var c in discovery.CoverageMatrix)
        {
            coverage.Add(new JsonObject
            {
                ["capability"] = c.Capability,
                ["status"] = c.Status,
                ["fulfilledBy"] = c.FulfilledBy,
                ["rationale"] = c.Rationale
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = 2,
            ["proposalId"] = proposalId,
            ["intent"] = intent,
            ["supported"] = false,
            ["missingCapabilities"] = new JsonArray(discovery.MissingCapabilities.Select(m => (JsonNode)JsonValue.Create(m)!).ToArray()),
            ["capabilityCoverage"] = coverage,
            ["recommendedComponents"] = new JsonArray(),
            ["compositionPlan"] = new JsonObject
            {
                ["target"] = "console",
                ["composedPacks"] = new JsonArray(),
                ["composedBlueprints"] = new JsonArray(),
                ["connections"] = new JsonArray()
            },
            ["generatedSourcePreview"] = new JsonArray(),
            ["approvalRequired"] = false,
            ["verification"] = new JsonObject
            {
                ["command"] = "otter check",
                ["syntaxValid"] = false,
                ["diagnostics"] = new JsonArray { (JsonNode)JsonValue.Create($"No compatible components found in offline library catalog for: '{intent}'")! }
            },
            ["unresolvedLimitations"] = new JsonArray
            {
                (JsonNode)JsonValue.Create("Otter 1.0.1 offline catalog does not provide building blocks for the requested domain.")!
            }
        };
    }

    public static string FormatGraphExplanation(CompositionGraph graph, string proposalId, List<PackContractInfo> packs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Explainable Multi-Pack Composition Graph ({proposalId}) ===");
        sb.AppendLine();
        sb.AppendLine("1. Participating Component Contracts:");
        foreach (var p in packs)
        {
            sb.AppendLine($"   * {p.Id} v{p.Version}");
            sb.AppendLine($"     Prefix:       {p.VariablePrefix}");
            sb.AppendLine($"     Runtime:      {p.RequiresOtter} (target platform supported)");
            sb.AppendLine($"     Capabilities: {string.Join(", ", p.Capabilities)}");
            sb.AppendLine($"     Operations:   {string.Join(", ", p.Operations.Select(o => o.Name))}");
        }
        sb.AppendLine();

        sb.AppendLine("2. Deterministic Execution DAG (Topological Order):");
        for (int i = 0; i < graph.TopologicalOrder.Count; i++)
        {
            string nodeId = graph.TopologicalOrder[i];
            var node = graph.Nodes.First(n => n.Id == nodeId);
            sb.AppendLine($"   Step {i + 1}: [{node.PackId}]::{node.OperationName} -> stores into '{node.ResultVar}'");
        }
        sb.AppendLine();

        sb.AppendLine("3. Port Bindings & Type Invariants:");
        foreach (var edge in graph.Edges)
        {
            string kind = edge.Kind == EdgeKind.Data ? "[DATA]" : "[ORDERING]";
            string status = edge.Compatible ? "[OK]" : "[FAIL]";
            string adapter = edge.Adapter != null ? $" (adapter: {edge.Adapter.Id})" : "";
            sb.AppendLine($"   {status} {kind} {edge.FromNodeId}.{edge.FromPort} ({edge.FromType}) ---> {edge.ToNodeId}.{edge.ToPort} ({edge.ToType}){adapter}");
        }
        sb.AppendLine();

        sb.AppendLine("4. Visual Data Flow (Mermaid):");
        sb.AppendLine("   ```mermaid");
        sb.AppendLine("   graph TD");
        foreach (var node in graph.Nodes)
        {
            sb.AppendLine($"       {node.Id}[\"{node.PackId}<br/>{node.OperationName}()\"]");
        }
        foreach (var edge in graph.Edges)
        {
            if (edge.Kind == EdgeKind.Data)
            {
                string adp = edge.Adapter != null ? $" ({edge.Adapter.Id})" : "";
                sb.AppendLine($"       {edge.FromNodeId} -->|\"{edge.FromType} -> {edge.ToType}{adp}\"| {edge.ToNodeId}");
            }
            else
            {
                sb.AppendLine($"       {edge.FromNodeId} -.->|\"order\"| {edge.ToNodeId}");
            }
        }
        sb.AppendLine("   ```");
        sb.AppendLine();

        sb.AppendLine("5. Scoping Safety Analysis:");
        sb.AppendLine("   - All variable prefixes verified non-overlapping.");
        sb.AppendLine("   - Orchestrator variables use isolated '_orch_' prefix.");
        sb.AppendLine("   - Zero risk of caller variable clashing under Otter 1.0.1 dynamic scoping.");

        return sb.ToString();
    }

    // =========================================================================
    // 10. Capability & Intent Discovery Fallback
    // =========================================================================

    private sealed class DiscoveryOutcome
    {
        public bool Supported { get; set; } = true;
        public List<PackContractInfo> SelectedPacks { get; } = new();
        public List<CoverageItem> CoverageMatrix { get; } = new();
        public List<string> Diagnostics { get; } = new();
        public List<string> MissingCapabilities { get; } = new();
    }

    private static DiscoveryOutcome MatchIntentCapabilities(string intent, ComponentCatalog catalog)
    {
        var outcome = new DiscoveryOutcome();
        string lower = intent.ToLowerInvariant();
        var tokens = Regex.Matches(lower, @"[a-z0-9\-]+").Select(m => m.Value).ToHashSet();

        // Unsupported domain filters (GPU, neural networks, blockchains, kernel)
        var unsupported = new Dictionary<string, string[]>
        {
            ["gpu-acceleration"] = new[] { "gpu", "cuda", "vulkan", "opencl", "directx", "metal" },
            ["neural-networks"] = new[] { "neural", "deep-learning", "pytorch", "tensorflow", "transformer", "whisper", "llm" },
            ["audio-dsp"] = new[] { "audio-dsp", "dsp", "vst", "vst3", "synthesizer", "synth", "midi" },
            ["blockchain"] = new[] { "blockchain", "crypto", "bitcoin", "ethereum", "solana", "mining" },
            ["kernel-embedded"] = new[] { "kernel-driver", "kernel", "fpga", "embedded", "firmware" },
            ["websocket-sync"] = new[] { "websocket", "real-time-sync", "socket-io" }
        };

        foreach (var (domain, kwList) in unsupported)
        {
            if (kwList.Any(k => tokens.Contains(k) || lower.Contains(k)))
            {
                outcome.Supported = false;
                outcome.MissingCapabilities.Add(domain);
                outcome.CoverageMatrix.Add(new CoverageItem(
                    domain,
                    "unsupported",
                    null,
                    $"Otter 1.0.1 does not provide library building blocks for {domain}."
                ));
                outcome.Diagnostics.Add($"[UNSUPPORTED_CAPABILITY] Requested domain '{domain}' is not provided by Otter standard library.");
            }
        }

        if (!outcome.Supported) return outcome;

        // Match capabilities across any registered catalog pack
        foreach (var pack in catalog.Packs.Values)
        {
            bool matches = false;
            foreach (var cap in pack.Capabilities)
            {
                string capLower = cap.ToLowerInvariant();
                bool tokenMatch = tokens.Any(t =>
                    capLower.Contains(t) ||
                    (t.Length >= 4 && capLower.Contains(t.Substring(0, 4))) ||
                    (t.Length >= 5 && capLower.Contains(t.Substring(0, 5))) ||
                    (t.Length >= 6 && capLower.Contains(t.Substring(0, 6))));

                if (tokenMatch)
                {
                    matches = true;
                    outcome.CoverageMatrix.Add(new CoverageItem(cap, "implemented", pack.Id, $"Fulfilled by {pack.Id} contract operations."));
                }
            }

            if (matches && !outcome.SelectedPacks.Contains(pack))
            {
                outcome.SelectedPacks.Add(pack);
            }
        }

        if (outcome.SelectedPacks.Count == 0)
        {
            outcome.Supported = false;
            outcome.MissingCapabilities.Add("unmatched-intent");
            outcome.CoverageMatrix.Add(new CoverageItem(
                "unmatched-intent",
                "unsupported",
                null,
                $"Intent '{intent}' does not match any verified building block capabilities in the catalog."
            ));
            outcome.Diagnostics.Add($"[UNMATCHED_INTENT] Intent '{intent}' does not match available offline pack capabilities.");
        }

        return outcome;
    }
}
