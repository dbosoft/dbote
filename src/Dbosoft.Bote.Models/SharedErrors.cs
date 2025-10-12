using System.Security.Authentication;

// ReSharper disable UnusedMember.Global

namespace Dbosoft.Bote.Models;

internal partial class Errors
{
    public static ArgumentException CannotBothBeNotNull(string param0, string param1) =>
        new($"{param0} and {param1} cannot both be set");

    public static ArgumentOutOfRangeException MustBeGreaterThanOrEqualTo(string paramName, long value)
        => new(paramName, $"Value must be greater than or equal to {value}");

    public static ArgumentOutOfRangeException MustBeLessThanOrEqualTo(string paramName, long value)
        => new(paramName, $"Value must be less than or equal to {value}");

    public static ArgumentOutOfRangeException MustBeBetweenInclusive(
        string paramName,
        long lower,
        long upper,
        long actual)
        => new(paramName, $"Value must be between {lower} and {upper} inclusive, not {actual}");

    public static ArgumentOutOfRangeException MustBeGreaterThanValueOrEqualToOtherValue(
        string paramName,
        long value0,
        long value1)
        => new(paramName, $"Value must be greater than {value0} or equal to {value1}");

    public static ArgumentException StreamMustBeReadable(string paramName) => new("Stream must be readable", paramName);

    public static InvalidOperationException StreamMustBeAtPosition0() => new("Stream must be set to position 0");

    public static InvalidOperationException TokenCredentialsRequireHttps() =>
        new("Use of token credentials requires HTTPS");


    public static InvalidOperationException SasMissingData(string paramName) =>
        new($"SAS is missing required parameter: {paramName}");

    public static InvalidOperationException SasDataNotAllowed(string paramName, string paramNameNotAllowed)
        => new($"SAS cannot have the {paramNameNotAllowed} parameter when the {paramName} parameter is present");

    public static InvalidOperationException SasDataInConjunction(string paramName, string paramName2)
        => new($"SAS cannot have the following parameters specified in conjunction: {paramName}, {paramName2}");

    public static InvalidOperationException SasNamesNotMatching(string builderParam, string builderName, string clientParam)
        => new($"SAS Uri cannot be generated. {builderName}.{builderParam} does not match {clientParam} in the Client. " +
               $"{builderName}.{builderParam} must either be left empty or match the {clientParam} in the Client");

    public static InvalidOperationException SasNamesNotMatching(string builderParam, string builderName)
        => new($"SAS Uri cannot be generated. {builderName}.{builderParam} does not match snapshot value in the URI in the Client. " +
               $"{builderName}.{builderParam} must either be left empty or match the snapshot value in the URI in the Client");

    public static InvalidOperationException SasServiceNotMatching(string builderParam, string builderName, string expectedService)
        => new($"SAS Uri cannot be generated. {builderName}.{builderParam} does specify {expectedService}. " +
               $"{builderName}.{builderParam} must either specify {expectedService} or specify all Services are accessible in the value.");

    public static InvalidOperationException SasClientMissingData(string paramName)
        => new($"SAS Uri cannot be generated. {paramName} in the client has not been set");

    public static InvalidOperationException SasBuilderEmptyParam(string builderName, string paramName, string sasType)
        => new($"SAS Uri cannot be generated. {builderName}.{paramName} cannot be set to create a {sasType} SAS.");

    public static InvalidOperationException SasIncorrectResourceType(string builderName, string builderParam, string value, string clientName)
        => new($"SAS Uri cannot be generated. Expected {builderName}.{builderParam} to be set to {value} to generate" +
               $"the respective SAS for the client, {clientName}");

    public static ArgumentException InvalidPermission(char s) => new($"Invalid permission: '{s}'");

    public static ArgumentException ParsingHttpRangeFailed() => new("Could not parse the serialized range.");

    public static AccessViolationException UnableAccessArray() => new("Unable to get array from memory pool");

    public static NotImplementedException NotImplemented() => new();

    public static AuthenticationException InvalidCredentials(string fullName) =>
        new($"Cannot authenticate credentials with {fullName}");

    public static ArgumentException SeekOutsideBufferRange(long index, long inclusiveRangeStart, long exclusiveRangeEnd)
        => new($"Tried to seek ouside buffer range. Gave index {index}, range is [{inclusiveRangeStart},{exclusiveRangeEnd}).");

    public static ArgumentException VersionNotSupported(string paramName)
        => new($"The version specified by {paramName} is not supported by this library.");

    public static ArgumentException TransactionalHashingNotSupportedWithClientSideEncryption()
        => new("Client-side encryption and transactional hashing are not supported at the same time.");

 
    public static class ClientSideEncryption
    {
        public static InvalidOperationException ClientSideEncryptionVersionNotSupported(string versionString = default)
            => new("This library does not support the given version of client-side encryption." +
                versionString == default ? "" : $" Version ID = {versionString}");

        public static InvalidOperationException TypeNotSupported(Type type)
            => new(
                $"Client-side encryption is not supported for type \"{type.FullName}\". " +
                "Please use a supported client type or create this client without specifying client-side encryption options.");

        public static InvalidOperationException MissingRequiredEncryptionResources(params string[] resourceNames)
            => new("Cannot encrypt without specifying " + string.Join(",", resourceNames.AsEnumerable()));

        public static ArgumentException KeyNotFound(string keyId) => new($"Resolution of id {keyId} returned null.");

        public static ArgumentException BadEncryptionAgent(string agent)
            => new("Invalid Encryption Agent. This version of the client library does not understand" +
                   $"the Encryption Agent protocol \"{agent}\" set on the blob.");

        public static ArgumentException BadEncryptionAlgorithm(string algorithm)
            => new($"Invalid Encryption Algorithm \"{algorithm}\" found on the resource. This version of the client" +
                   "library does not support the given encryption algorithm.");

        public static InvalidOperationException MissingEncryptionMetadata(string field)
            => new($"Missing field \"{field}\" in encryption metadata");
    }
}