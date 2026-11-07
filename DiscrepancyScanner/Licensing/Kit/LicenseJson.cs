using System.IO;
using System.Runtime.Serialization.Json;

namespace ToolLicensing
{
    /// <summary>
    /// JSON in and out, with nothing but the framework.
    ///
    /// What is signed is the exact byte string produced here, and the same bytes travel in the
    /// license file, so it does not matter that the serializer's output is not "canonical" - the
    /// verifier never re-serializes, it checks the signature over the bytes it was given.
    /// </summary>
    public static class LicenseJson
    {
        public static byte[] Serialize<T>(T value)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return stream.ToArray();
            }
        }

        public static T Deserialize<T>(byte[] bytes)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (MemoryStream stream = new MemoryStream(bytes))
            {
                return (T)serializer.ReadObject(stream);
            }
        }
    }
}
