using FluentAssertions;
using Patients.Contracts;
using Patients.Features.RegisterPatient;
using Xunit;

namespace MedClinic.Integration.Tests.Patients;

/// <summary>
/// Unit tests for RegisterPatientValidator.
/// No database needed — validators are pure functions and can be tested in isolation.
/// These tests verify the business rule: consent to data processing is mandatory.
/// </summary>
public sealed class RegisterPatientValidatorTests
{
    private readonly RegisterPatientValidator _validator = new(TimeProvider.System);

    [Fact]
    public void Validate_WithConsentTrue_IsValid()
    {
        var command = new RegisterPatientCommand(
            FirstName: "Alice",
            LastName: "Nguyen",
            DateOfBirth: new DateOnly(1990, 1, 1),
            ContactPhone: "+64 21 555 0001",
            ConsentToDataProcessing: true);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithConsentFalse_Fails()
    {
        var command = new RegisterPatientCommand(
            FirstName: "Bob",
            LastName: "Smith",
            DateOfBirth: new DateOnly(1970, 6, 1),
            ContactPhone: "+64 21 555 0002",
            ConsentToDataProcessing: false);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == nameof(RegisterPatientCommand.ConsentToDataProcessing));
    }

    [Fact]
    public void Validate_WithFutureDateOfBirth_Fails()
    {
        var command = new RegisterPatientCommand(
            FirstName: "Future",
            LastName: "Person",
            DateOfBirth: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            ContactPhone: "+64 21 555 0003",
            ConsentToDataProcessing: true);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == nameof(RegisterPatientCommand.DateOfBirth));
    }

    [Fact]
    public void Validate_WithEmptyFirstName_Fails()
    {
        var command = new RegisterPatientCommand(
            FirstName: "",
            LastName: "Smith",
            DateOfBirth: new DateOnly(1980, 1, 1),
            ContactPhone: "+64 21 555 0004",
            ConsentToDataProcessing: true);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == nameof(RegisterPatientCommand.FirstName));
    }
}
