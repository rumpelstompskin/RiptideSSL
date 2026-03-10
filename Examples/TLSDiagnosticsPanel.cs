using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TLSDiagnosticsPanel : MonoBehaviour
{
    [SerializeField]
    private Button connectButton;

    [SerializeField]
    private Button disconnectButton;

    [SerializeField]
    private Button startTLSTCPServerButton;

    [SerializeField]
    private Button stopTLSTCPServerButton;

    [SerializeField]
    private TMP_InputField addressInputfield;

    [SerializeField]
    private TMP_InputField portInputfield;

    [SerializeField]
    private ServerManager serverManager;

    [SerializeField]
    private ClientManager clientManager;

    [SerializeField]
    private TMP_Text tLSDebugText;

    public void Connect()
    {
        clientManager.addressPort = $"{addressInputfield.text}:{portInputfield.text}";
        clientManager.ConnectTLSClientAsync();
        connectButton.gameObject.SetActive(false);
        disconnectButton.gameObject.SetActive(true);
    }

    public void Disconnect()
    {
        clientManager.DisconnectClient();
        disconnectButton.gameObject.SetActive(false);
        connectButton.gameObject.SetActive(true);
    }

    public void StartTLSServer()
    {
        serverManager.StartTlsTcpServer();
        GetTLSData();
        startTLSTCPServerButton.gameObject.SetActive(false);
        stopTLSTCPServerButton.gameObject.SetActive(true);
    }

    public void StopTLSServer()
    {
        serverManager.StopServer();
        tLSDebugText.text = "";
        stopTLSTCPServerButton.gameObject.SetActive(false);
        startTLSTCPServerButton.gameObject.SetActive(true);
    }

    public void GetTLSData()
    {
        tLSDebugText.text = $"Port: {serverManager.PORT} \n" +
            $"Max Clients: {serverManager.MAX_CLIENTCOUNT} \n" +
            $"Cert Name: {serverManager.GetTransport.CERT_NAME} \n" +
            $"Cert PW: {serverManager.GetTransport.CERT_PW} \n" +
            $"Has private key: {serverManager.GetTransport.ServerCertificate.HasPrivateKey}";
    }
}
