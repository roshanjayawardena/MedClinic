using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace MedClinic.Architecture.Tests;

/// <summary>
/// Verifies handler class conventions.
/// Golden rule #6: "sealed handlers, file-scoped namespaces, primary constructors throughout."
/// </summary>
public sealed class HandlerConventionTests
{
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

    public static IEnumerable<object[]> ModuleAssemblies =>
        ModuleRuntimeAssemblies.Select(a => new object[] { a });

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void Handlers_MustBe_Sealed(Assembly assembly)
    {
        // Find every concrete class that implements the Mediator handler interfaces.
        // We match on name prefix to avoid a hard reference to Mediator.Abstractions here.
        var handlerTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType &&
                (i.GetGenericTypeDefinition().Name.StartsWith("IRequestHandler") ||
                 i.GetGenericTypeDefinition().Name.StartsWith("INotificationHandler"))))
            .ToList();

        handlerTypes.Should().AllSatisfy(
            t => t.IsSealed.Should().BeTrue(
                because: $"{t.FullName} must be sealed (AGENTS.md golden rule #6)"),
            because: $"all handlers in {assembly.GetName().Name} must be sealed");
    }

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void Handlers_MustLive_In_Features_Namespace(Assembly assembly)
    {
        var violators = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType &&
                (i.GetGenericTypeDefinition().Name.StartsWith("IRequestHandler") ||
                 i.GetGenericTypeDefinition().Name.StartsWith("INotificationHandler"))))
            .Where(t => t.Namespace is not null && !t.Namespace.Contains(".Features."))
            .Select(t => t.FullName!)
            .ToList();

        violators.Should().BeEmpty(
            because: "handlers must live in a Features subfolder per the vertical-slice convention");
    }
}
