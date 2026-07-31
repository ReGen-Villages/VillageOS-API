using FluentAssertions;
using vos.Service.Shared.Validation;
using Xunit;

namespace vos.Service.Shared.Tests.Validation;

public class HttpMethodValidatorTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("CONNECT")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    [InlineData("PATCH")]
    public void IsSupportedMethod_KnownMethod_ReturnsTrue(string method)
    {
        HttpMethodValidator.IsSupportedMethod(method).Should().BeTrue();
    }

    [Theory]
    [InlineData("get")]
    [InlineData("Get")]
    [InlineData("PaTcH")]
    public void IsSupportedMethod_IsCaseInsensitive(string method)
    {
        HttpMethodValidator.IsSupportedMethod(method).Should().BeTrue();
    }

    [Theory]
    [InlineData("BREW")]      // April Fool's HTCPCP method; not in the set
    [InlineData("PROPFIND")]  // WebDAV; not in the set
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("GET ")]      // trailing space — HashSet lookup is exact match (ignoring case only)
    public void IsSupportedMethod_UnknownOrMalformed_ReturnsFalse(string method)
    {
        HttpMethodValidator.IsSupportedMethod(method).Should().BeFalse();
    }
}
