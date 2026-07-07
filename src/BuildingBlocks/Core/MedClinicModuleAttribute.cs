namespace Core;

/// <summary>
/// Assembly-level attribute that marks a module for auto-discovery by the host.
/// Place on the runtime project's assembly (not Contracts).
/// Order controls the registration sequence — lower numbers register first.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class MedClinicModuleAttribute(Type moduleType, int order = 100) : Attribute
{
    public Type ModuleType { get; } = moduleType;
    public int Order { get; } = order;
}
