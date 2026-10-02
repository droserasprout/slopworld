using Google.Protobuf;

namespace SlopWorld.Tests
{
    static class ProtobufFixtureTests
    {
        public static void UnknownFieldsAreRejectedByDefault()
        {
            AssertEx.Throws<InvalidProtocolBufferException>(() =>
                ProtobufFixtures.Read<Wire.AgentTemplate>(JVal.Parse("{\"versoin\":42}")), "misspelled fixture fields cannot silently disappear");
        }
    }
}
