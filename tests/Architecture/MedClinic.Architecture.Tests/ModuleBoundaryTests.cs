using System.Reflection;
using FluentAssertions;
using Xunit;

namespace MedClinic.Architecture.Tests;

/// <summary>
/// Verifies that module runtime projects never reference each other's runtime assemblies.
/// Cross-module communication is only permitted through *.Contracts projects.
/// Golden rule #10: "Modules reference only each other's .Contracts — never the runtime project."
/// </summary>
public sealed class ModuleBoundaryTests
{
    // Each entry is one module's runtime assembly, identified by its marker type.
    private static readonly Assembly[] ModuleRuntimeAssemblies =
    [
        typeof(Patients.PatientsModule).Assembly,
        typeof(Appointments.AppointmentsModule).Assembly,
        typeof(Encounters.EncountersModule).Assembly,
        typeof(Prescriptions.PrescriptionsModule).Assembly,
        typeof(Identity.IdentityModule).Assembly,
        typeof(Billing.BillingModule).Assembly,
        typeof(Notifications.NotificationsModule).Assembly,
    ];

    private static readonly HashSet<string> RuntimeAssemblyNames =
        ModuleRuntimeAssemblies.Select(a => a.GetName().Name!).ToHashSet();

    [Fact]
    public void Modules_ShouldNot_Reference_Each_Others_Runtime_Assembly()
    {
        var violations = new List<string>();

        foreach (var assembly in ModuleRuntimeAssemblies)
        {
            var assemblyName = assembly.GetName().Name!;

            var illegalReferences = assembly
                .GetReferencedAssemblies()
                .Select(r => r.Name!)
                .Where(name => RuntimeAssemblyNames.Contains(name) && name != assemblyName)
                .ToList();

            foreach (var illegal in illegalReferences)
                violations.Add($"{assemblyName} → {illegal}");
        }

        violations.Should().BeEmpty(
            because: "modules must communicate only through *.Contracts assemblies, " +
                     "never through each other's runtime projects");
    }
}
