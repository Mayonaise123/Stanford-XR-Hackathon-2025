using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

[RequireComponent(typeof(VideoPlayer))]
public class ReferenceVideoPlayer : MonoBehaviour
{
    [Header("Output")]
    [Tooltip("RawImage that shows the reference video in the UI.")]
    public RawImage outputImage;

    [Tooltip("RenderTexture that the VideoPlayer will render into.")]
    public RenderTexture targetTexture;

    private VideoPlayer vp;

    void Awake()
    {
        vp = GetComponent<VideoPlayer>();

        // Make sure the player is set up correctly
        vp.playOnAwake = false;
        vp.isLooping = true; // we will usually loop the reference videos

        if (targetTexture != null)
        {
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.targetTexture = targetTexture;
        }

        if (outputImage != null && targetTexture != null)
        {
            outputImage.texture = targetTexture;
        }
    }

    /// <summary>
    /// Play the given clip as a looping reference video.
    /// </summary>
    public void PlayReferenceClip(VideoClip clip)
    {
        if (clip == null || vp == null)
        {
            Debug.LogWarning("ReferenceVideoPlayer: No clip or VideoPlayer.");
            return;
        }

        vp.Stop();
        vp.clip = clip;
        vp.isLooping = true;
        vp.Play();
    }

    /// <summary>
    /// Stop playback and clear the clip.
    /// </summary>
    public void StopReference()
    {
        if (vp == null) return;

        vp.Stop();
        vp.clip = null;
    }
}
