using UnityEngine;
using UnityEngine.UI;

public class Main : MonoBehaviour
{
    public Simulation simulation;
    public GameObject canvas;

    public Toggle errorCorrectionToggle;
    public Toggle correctionSmoothingToggle;
    public Toggle redundantInputToggle;
    public Toggle serverPlayerToggle;
    public Text packetLossLabel;
    public Slider packetLossSlider;
    public Text networkLatencyLabel;
    public Slider networkLatencySlider;
    public Text snapshotRateLabel;
    public Slider snapshotRateSlider;

    public void Start()
    {
        packetLossSlider.value = simulation.packetLoss;
        networkLatencySlider.value = simulation.networkLatency;
        snapshotRateSlider.value = Mathf.Log(simulation.snapshotRate, 2.0f);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F12))
        {
            canvas.SetActive(!canvas.activeSelf);
        }
    }

    public void OnErrorCorrectionToggled(bool value)
    {
        correctionSmoothingToggle.interactable = value;
        simulation.errorCorrection = value;
    }

    public void OnCorrectionSmoothingToggled(bool value)
    {
        simulation.correctionSmoothing = value;
    }

    public void OnRedundantInputToggled(bool value)
    {
        simulation.redundantInput = value;
    }

    public void OnServerPlayerToggle(bool value)
    {
        Renderer renderer = simulation.serverPlayer.GetComponent<Renderer>();
        renderer.enabled = value;
    }

    public void OnPacketLossSliderChanged(float value)
    {
        packetLossLabel.text =
            string.Format("Packet Loss - {0:F1}%", value * 100.0f);
        simulation.packetLoss = value;
    }

    public void OnNetworkLatencySliderChanged(float value)
    {
        networkLatencyLabel.text =
            string.Format("Network Latency - {0}ms", (int)(value * 1000.0f));
        simulation.networkLatency = value;
    }

    public void OnSnapshotRateSliderChanged(float value)
    {
        uint rate = (uint)Mathf.Pow(2, value);
        snapshotRateLabel.text =
            string.Format("Snapshot Rate - {0}hz", 64 / rate);
        simulation.snapshotRate = rate;
    }
}
