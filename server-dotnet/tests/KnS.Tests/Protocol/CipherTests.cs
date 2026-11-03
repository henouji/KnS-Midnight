using System.Text;
using KnS.Protocol.Crypto;
using KnS.Protocol.Json;

namespace KnS.Tests.Protocol;

public class CipherTests
{
    [Fact]
    public void GameGateway_decrypts_a_body_encrypted_by_the_python_server()
    {
        // gamegw.encrypt({"protocol": 51, "params": {"hub_did": "abc"}})
        byte[] body = Encoding.ASCII.GetBytes("YOD9JgrRy8iTf0NYIERPK5scIfcJSmOBoaH11Sjpqon+c6hb3Jp59kdEwFq29JBO");

        var request = WireJson.ParseObject(GameGatewayCipher.Decrypt(body))!;

        Assert.Equal(51, request["protocol"]!.GetValue<int>());
        Assert.Equal("abc", request["params"]!["hub_did"]!.GetValue<string>());
    }

    [Fact]
    public void GameGateway_round_trips_and_ignores_line_breaks_in_base64()
    {
        byte[] plaintext = Encoding.UTF8.GetBytes("""{"protocol":60,"params":{"user_no":10001}}""");
        string encrypted = Encoding.ASCII.GetString(GameGatewayCipher.Encrypt(plaintext));
        string wrapped = encrypted[..10] + "\r\n" + encrypted[10..] + "\n";

        Assert.Equal(plaintext, GameGatewayCipher.Decrypt(Encoding.ASCII.GetBytes(wrapped)));
    }

    [Fact]
    public void ActiveUser_decrypts_a_body_encrypted_by_the_python_server()
    {
        // activeuser.encrypt_response("1729876543", {"api": "get_did", "android_id": "abc"})
        byte[] body = Encoding.ASCII.GetBytes("BXCgItHa7VAUOFIMMsN5CfyaBEDv/DSjyXuUwho+cHrLAaZlKobFUTYrg7moltko");

        var request = WireJson.ParseObject(ActiveUserCipher.Decrypt("1729876543", body))!;

        Assert.Equal("get_did", request["api"]!.GetValue<string>());
        Assert.Equal("abc", request["android_id"]!.GetValue<string>());
    }

    [Fact]
    public void ActiveUser_auth_key_is_the_md5_of_the_plaintext()
    {
        Assert.Equal("5d41402abc4b2a76b9719d911017c592", ActiveUserCipher.ComputeAuthKey("hello"u8));
    }
}
