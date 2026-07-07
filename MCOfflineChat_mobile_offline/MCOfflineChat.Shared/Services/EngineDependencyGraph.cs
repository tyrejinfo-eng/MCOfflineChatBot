using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// v1.1.58: Dependency graph for engine startup ordering.
/// Engines declare dependencies, and startup proceeds in topological order.
/// Detects circular dependencies and breaks them gracefully.
/// </summary>
public sealed class EngineDependencyGraph
{
    private readonly Dictionary<string, HashSet<string>> _dependencies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _dependents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Declare that <paramref name="engine"/> depends on <paramref name="dependsOn"/>.</summary>
    public void AddDependency(string engine, string dependsOn)
    {
        if (!_dependencies.TryGetValue(engine, out var deps))
        {
            deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _dependencies[engine] = deps;
        }
        deps.Add(dependsOn);

        // Also track the reverse (who depends on whom)
        if (!_dependents.TryGetValue(dependsOn, out var revDeps))
        {
            revDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _dependents[dependsOn] = revDeps;
        }
        revDeps.Add(engine);
    }

    /// <summary>Get direct dependencies for an engine.</summary>
    public IReadOnlySet<string> GetDependencies(string engine)
    {
        return _dependencies.TryGetValue(engine, out var deps) ? deps : new HashSet<string>();
    }

    /// <summary>Get engines that depend on the given engine.</summary>
    public IReadOnlyList<string> GetDependents(string engine)
    {
        return _dependents.TryGetValue(engine, out var deps) ? deps.ToList() : [];
    }

    /// <summary>
    /// Returns engines in topological startup order (dependencies first).
    /// Accepts all known engine names so engines without declared dependencies are included.
    /// </summary>
    public List<string> GetStartOrder(IEnumerable<string>? allEngines = null)
    {
        // Collect all known engine names from dependencies + provided list
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _dependencies)
        {
            all.Add(kvp.Key);
            foreach (var dep in kvp.Value)
                all.Add(dep);
        }
        if (allEngines != null)
        {
            foreach (var e in allEngines)
                all.Add(e);
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        var inStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var engine in all)
        {
            if (!visited.Contains(engine))
                TopologicalSort(engine, visited, inStack, result);
        }

        return result;
    }

    /// <summary>Detect if adding a dependency would create a cycle.</summary>
    public bool HasCircularDependency(string engine, string dependsOn)
    {
        // Check if dependsOn transitively depends on engine
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return HasPath(dependsOn, engine, visited);
    }

    private bool HasPath(string from, string to, HashSet<string> visited)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!visited.Add(from))
            return false;
        if (_dependencies.TryGetValue(from, out var deps))
        {
            foreach (var dep in deps)
            {
                if (HasPath(dep, to, visited))
                    return true;
            }
        }
        return false;
    }

    private void TopologicalSort(string engine, HashSet<string> visited, HashSet<string> inStack, List<string> result)
    {
        if (inStack.Contains(engine))
        {
            SglLogger.Warning("[DependencyGraph] Circular dependency detected involving {Engine}", engine);
            return; // Break circular dependency gracefully
        }

        if (visited.Contains(engine)) return;

        inStack.Add(engine);

        if (_dependencies.TryGetValue(engine, out var deps))
        {
            foreach (var dep in deps)
                TopologicalSort(dep, visited, inStack, result);
        }

        inStack.Remove(engine);
        visited.Add(engine);
        result.Add(engine);
    }
}
