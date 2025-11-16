using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.Video;

[Serializable]
public class SignLesson
{
    public string id;               // internal id, for example "egg"
    public string displayName;      // user facing name
    public string yoloLabel;        // expected YOLO label, for example "egg"
    public VideoClip referenceVideo; // reference video for this sign
}

public class SignLessonManager : MonoBehaviour
{
    [Header("Lesson data")]
    [Tooltip("Sign names in order, for example: egg, mushroom, hamburger, fish, bread.")]
    public List<string> signNames = new List<string>();

    [Tooltip("Reference videos in the same order as signNames.")]
    public List<VideoClip> signVideos = new List<VideoClip>();

    [Header("UI")]
    public TextMeshPro signNameText;
    public TextMeshPro progressText;
    public TextMeshPro statusText;

    [Tooltip("Reference video player wrapper that shows the videos in the UI.")]
    public ReferenceVideoPlayer referenceVideoPlayer;

    [Header("Detection source")]
    public LLMVRCameraStreamer cameraStreamer;

    [Header("Confusion logic")]
    [Tooltip("Seconds of continuous confusion before we trigger Gemini help.")]
    public float confusionSecondsThreshold = 2.5f;

    [Header("Scoring")]
    [Tooltip("Minimum confidence to count a detection as correct.")]
    public float minConfidence = 0.6f;

    [Tooltip("How many recent samples to consider in the sliding window.")]
    public int windowSize = 40;

    [Tooltip("Required fraction of correct samples in the window to pass the lesson.")]
    public float requiredAccuracy = 0.5f; // 50 percent

    [Tooltip("Minimum number of samples before we try to evaluate.")]
    public int minSamplesToEvaluate = 15;

    [Tooltip("Seconds to show success message and 3D model before moving to next sign.")]
    public float successHoldSeconds = 5f;

    [Header("3D reward models")]
    [Tooltip("One 3D model per sign, in the same order as signNames.")]
    public List<GameObject> rewardModels = new List<GameObject>();

    [Header("Check images")]
    [Tooltip("One check image per sign, in the same order as signNames.")]
    public List<GameObject> checkImages = new List<GameObject>();

    private List<SignLesson> lessons = new List<SignLesson>();
    private int currentLessonIndex = 0;

    private Queue<bool> recentCorrect = new Queue<bool>();
    private int correctCount = 0;

    private bool lessonCompleted = false;
    private float lessonCompleteTime = 0f;

    private float confusedTimer = 0f;
    private bool helpTriggeredThisLesson = false;

    void Awake()
    {
        BuildLessonsFromLists();
    }

    void Start()
    {
        if (lessons.Count == 0)
        {
            Debug.LogError("SignLessonManager: no lessons configured. Check signNames and signVideos.");
            enabled = false;
            return;
        }

        if (cameraStreamer == null)
        {
            Debug.LogError("SignLessonManager: cameraStreamer is not assigned.");
            enabled = false;
            return;
        }

        HideAllRewards();
        HideAllChecks();
        LoadCurrentLesson();
    }

    private void BuildLessonsFromLists()
    {
        lessons.Clear();

        if (signNames.Count == 0 ||
            signVideos.Count == 0 ||
            signNames.Count != signVideos.Count)
        {
            Debug.LogError("SignLessonManager: signNames and signVideos must have the same non zero length.");
            return;
        }

        for (int i = 0; i < signNames.Count; i++)
        {
            string name = signNames[i];
            VideoClip clip = signVideos[i];

            string id = name.Trim().ToLower().Replace(" ", "_");
            string yoloLabel = id;

            var lesson = new SignLesson
            {
                id = id,
                displayName = name,
                yoloLabel = yoloLabel,
                referenceVideo = clip
            };

            lessons.Add(lesson);
        }

        Debug.Log($"SignLessonManager: built {lessons.Count} lessons.");
    }

    private void LoadCurrentLesson()
    {
        if (currentLessonIndex < 0 || currentLessonIndex >= lessons.Count)
        {
            // all signs completed
            if (statusText != null)
                statusText.text = "You have bought all these items!";

            if (referenceVideoPlayer != null)
            {
                referenceVideoPlayer.StopReference();
            }

            if (signNameText != null) signNameText.text = "";
            if (progressText != null) progressText.text = "";
            return;
        }

        SignLesson lesson = lessons[currentLessonIndex];

        // play reference video
        if (referenceVideoPlayer != null)
        {
            referenceVideoPlayer.PlayReferenceClip(lesson.referenceVideo);
        }

        if (signNameText != null)
        {
            int lessonNumber = currentLessonIndex + 1;
            int totalLessons = lessons.Count;
            signNameText.text = $"Sign {lessonNumber}/{totalLessons}: {lesson.displayName}";
        }

        ResetWindow();
        lessonCompleted = false;
        lessonCompleteTime = 0f;

        confusedTimer = 0f;
        helpTriggeredThisLesson = false;

        if (statusText != null)
        {
            statusText.text = "Try to copy this sign.";
        }

        Debug.Log($"SignLessonManager: loaded lesson {currentLessonIndex} ({lesson.displayName})");
    }

    private void ResetWindow()
    {
        recentCorrect.Clear();
        correctCount = 0;
        UpdateProgressText();
    }

    private void HideAllRewards()
    {
        if (rewardModels == null) return;

        for (int i = 0; i < rewardModels.Count; i++)
        {
            if (rewardModels[i] != null)
            {
                rewardModels[i].SetActive(false);
            }
        }
    }

    private void ShowRewardForCurrentLesson()
    {
        if (rewardModels == null) return;

        if (currentLessonIndex >= 0 && currentLessonIndex < rewardModels.Count)
        {
            GameObject model = rewardModels[currentLessonIndex];
            if (model != null)
            {
                model.SetActive(true);
            }
        }
    }

    private void HideAllChecks()
    {
        if (checkImages == null) return;

        for (int i = 0; i < checkImages.Count; i++)
        {
            if (checkImages[i] != null)
            {
                checkImages[i].SetActive(false);
            }
        }
    }

    private void ShowCheckForCurrentLesson()
    {
        if (checkImages == null) return;

        if (currentLessonIndex >= 0 && currentLessonIndex < checkImages.Count)
        {
            GameObject check = checkImages[currentLessonIndex];
            if (check != null)
            {
                check.SetActive(true);
            }
        }
    }

    void Update()
    {
        if (lessonCompleted)
        {
            if (Time.time - lessonCompleteTime >= successHoldSeconds)
            {
                currentLessonIndex++;
                LoadCurrentLesson();
            }
            return;
        }

        if (lessons.Count == 0) return;
        if (currentLessonIndex < 0 || currentLessonIndex >= lessons.Count) return;

        // EEG confusion
        bool confusedNow = (cameraStreamer != null && cameraStreamer.GetLatestEegConfused() == 1);
        if (confusedNow)
        {
            confusedTimer += Time.deltaTime;
        }
        else
        {
            confusedTimer = 0f;
        }

        string label = cameraStreamer.GetLatestLabel();
        float conf = cameraStreamer.GetLatestConfidence();

        if (string.IsNullOrEmpty(label) || label == "none")
            return;

        SignLesson lesson = lessons[currentLessonIndex];

        bool isCorrect = (label == lesson.yoloLabel) && (conf >= minConfidence);

        // sliding window
        recentCorrect.Enqueue(isCorrect);
        if (isCorrect) correctCount++;

        while (recentCorrect.Count > windowSize)
        {
            bool oldest = recentCorrect.Dequeue();
            if (oldest) correctCount--;
        }

        UpdateProgressText();

        // trigger Gemini help if wrong and confused long enough
        if (!isCorrect &&
            confusedTimer >= confusionSecondsThreshold &&
            !helpTriggeredThisLesson)
        {
            helpTriggeredThisLesson = true;

            if (statusText != null)
            {
                statusText.text = "This one looks tricky. Let me help you a bit more.";
            }

            if (cameraStreamer != null)
            {
                cameraStreamer.RequestGeminiHelp(lesson.id);
            }

            Debug.Log($"SignLessonManager: requested Gemini help for sign {lesson.id}");
        }

        // pass condition
        int sampleCount = recentCorrect.Count;
        if (sampleCount >= minSamplesToEvaluate)
        {
            float accuracy = (float)correctCount / sampleCount;
            if (accuracy >= requiredAccuracy)
            {
                lessonCompleted = true;
                lessonCompleteTime = Time.time;

                // show 3D reward model for this sign and keep it
                ShowRewardForCurrentLesson();

                // show check image for this sign and keep it
                ShowCheckForCurrentLesson();

                if (statusText != null)
                {
                    statusText.text = $"Nice job. Your sign for {lesson.displayName} looks good.";
                }

                Debug.Log($"SignLessonManager: lesson passed for {lesson.displayName} " +
                          $"(accuracy {accuracy:0.00}, samples {sampleCount})");
            }
        }
    }

    private void UpdateProgressText()
    {
        if (progressText == null) return;

        int sampleCount = recentCorrect.Count;
        int lessonNumber = currentLessonIndex + 1;
        int totalLessons = lessons.Count;

        if (sampleCount == 0)
        {
            progressText.text = $"Sign {lessonNumber}/{totalLessons} progress: try making this sign.";
        }
        else
        {
            float accuracy = (float)correctCount / sampleCount;
            int percent = Mathf.RoundToInt(accuracy * 100f);

            progressText.text =
                $"Sign {lessonNumber}/{totalLessons} progress: {percent}% correct.";
            // If you also want to show how many samples, use this instead:
            // progressText.text =
            //     $"Sign {lessonNumber}/{totalLessons} progress: {percent}% correct over {sampleCount} samples.";
        }
    }
}
