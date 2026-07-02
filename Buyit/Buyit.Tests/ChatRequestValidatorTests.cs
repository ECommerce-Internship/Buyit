using Buyit.Application.DTOs;
using Buyit.Application.Validators;
using FluentAssertions;
using Xunit;

namespace Buyit.Tests;

public class ChatRequestValidatorTests
{
    private readonly ChatRequestValidator _validator = new();

    [Fact]
    public void Validate_EmptyMessage_Fails()
    {
        var result = _validator.Validate(new ChatRequest("", null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "message");
    }

    [Fact]
    public void Validate_MessageOver2000Chars_Fails()
    {
        var tooLong = new string('a', 2001);

        var result = _validator.Validate(new ChatRequest(tooLong, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "message");
    }

    [Fact]
    public void Validate_MessageExactly2000Chars_Passes()
    {
        var atLimit = new string('a', 2000);

        var result = _validator.Validate(new ChatRequest(atLimit, null));

        result.IsValid.Should().BeTrue();   // 2000 is allowed; only OVER 2000 fails
    }

    [Fact]
    public void Validate_NullConversationId_Passes()
    {
        // conversationId is optional — absence must NOT trigger the GUID rule.
        var result = _validator.Validate(new ChatRequest("hi", null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidGuidConversationId_Passes()
    {
        var result = _validator.Validate(new ChatRequest("hi", Guid.NewGuid().ToString()));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_MalformedConversationId_Fails()
    {
        var result = _validator.Validate(new ChatRequest("hi", "not-a-guid"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "conversationId");
    }
}
