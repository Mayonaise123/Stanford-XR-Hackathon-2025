using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

[Serializable]
public class SignLesson
{
    public string id;              // internal id, for example "egg"
    public string displayName;     // user facing name
    public Sprite referenceSprite; // image to show
    public string yoloLabel;       // expected YOLO label, for example "egg"
}

public class SignLessonManager : MonoBehaviour
{
    [Header("Lesson data")]
    [Tooltip("Reference images in order, for example: egg, mushroom, hamburger, fish, bread.")]
    public List<Sprite> signSprites = new List<Sprite>();

    [Tooltip("Sign names in the same order as signSprites, for example: egg, mushroom, hamburger, fish, bread.")]
    public List<string> signNames = new List<string>();

    [Header("UI")]
    public Image referenceImage;
    public TextMeshPro signNameText;
    public TextMeshPro progressText;
    public TextMeshPro statusText;

    [Header("Detection source")]
    public LLMVRCameraStreamer cameraStreamer;

    [Header("Confusion logic")]
    [Tooltip("Seconds of continuous confusion before we trigger Gemini help")]
    public float confusionSecondsThreshold = 2.5f;

    [Header("Scoring")]
    [Tooltip("Minimum confidence to count a detection as correct")]
    public float minConfidence = 0.6f;

    [Tooltip("How many recent samples to consider in the sliding window")]
    public int windowSize = 40;

    [Tooltip("Required fraction of correct samples in the window to pass the lesson")]
    public float requiredAccuracy = 0.5f; // 50 percent

    [Tooltip("Minimum number of samples before we try to evaluate")]
    public int minSamplesToEvaluate = 15;

    [Tooltip("Seconds to show success message and 3D model before moving to next sign")]
    public float successHoldSeconds = 5f;

    [Header("3D reward models")]
    [Tooltip("One 3D model per sign, in the same order as signSprites/signNames.")]
    public List<GameObject> rewardModels = new List<GameObject>();

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
            Debug.LogError("SignLessonManager: no lessons configured. Check signSprites and signNames.");
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
        LoadCurrentLesson();
    }

    private void BuildLessonsFromLists()
    {
        lessons.Clear();

        if (signSprites.Count == 0 || signSprites.Count != signNames.Count)
        {
            Debug.LogError("SignLessonManager: signSprites and signNames must have the same non zero length.");
            return;
        }

        for (int i = 0; i < signSprites.Count; i++)
        {
            Sprite sprite = signSprites[i];
            string name = signNames[i];

            string id = name.Trim().ToLower().Replace(" ", "_");
            string yoloLabel = id;  // matching how you named YOLO classes

            var lesson = new SignLesson
            {
                id = id,
                displayName = name,
                referenceSprite = sprite,
                yoloLabel = yoloLabel
            };

            lessons.Add(lesson);
        }

        Debug.Log($"SignLessonManager: built {lessons.Count} lessons.");
    }

    private void LoadCurrentLesson()
    {
        HideAllRewards();

        if (currentLessonIndex < 0 || currentLessonIndex >= lessons.Count)
        {
            if (statusText != null)
                statusText.text = "You finished all signs. Nice work.";

            if (referenceImage != null) referenceImage.enabled = false;
            if (signNameText != null) signNameText.text = "";
            if (progressText != null) progressText.text = "";
            return;
        }

        SignLesson lesson = lessons[currentLessonIndex];

        if (referenceImage != null)
        {
            referenceImage.sprite = lesson.referenceSprite;
            referenceImage.enabled = true;
        }

        if (signNameText != null)
        {
            signNameText.text = lesson.displayName;
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
        HideAllRewards();

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

        // EEG confusion from server via cameraStreamer
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

        // add to sliding window
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

        // check pass condition
        int sampleCount = recentCorrect.Count;
        if (sampleCount >= minSamplesToEvaluate)
        {
            float accuracy = (float)correctCount / sampleCount;
            if (accuracy >= requiredAccuracy)
            {
                lessonCompleted = true;
                lessonCompleteTime = Time.time;

                // show 3D reward model for this sign
                ShowRewardForCurrentLesson();

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
        float accuracy = sampleCount > 0 ? (float)correctCount / sampleCount : 0f;
        float pct = accuracy * 100f;

        progressText.text = $"{correctCount}/{sampleCount} correct ({pct:0.0} percent)";
    }
}
