using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LoadingUIController : MonoBehaviour
{
    public static LoadingUIController Instance;

    [SerializeField] private Slider progressBar;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private Image image;
    [SerializeField] private Sprite[] _frames;

    private Coroutine _coAnimRoutine;

    private void Awake()
    {
        Instance = this;

        progressBar.value = 0f;
        progressText.text = "0%";

        image = GameObject.Find("LoadingUI/Image")?.GetComponent<Image>();

        Texture2D sheet = Resources.Load<Texture2D>("uieffects/textures/FX_UI_Indicator_02");
        if (sheet == null)
        {
            //Debug.LogError($"Not found in Resources");
            return;
        }

        _frames = Util.Slice(sheet, 6, 4, 1);
        if (_frames == null || _frames.Length == 0)
        {
            //Debug.LogError("Sprite slicing failed");
        }

        if (Managers.Scene.CurrentScene is GameScene scene || Managers.Scene.CurrentScene is PickScene pickScene)
        {
            SetProgress(1);
            StartAnimation();
        }
    }
    public void SetProgress(float value)
    {
        progressBar.value = value;
        progressText.text = $"{(value * 100f):0}%";
    }

    public void StartAnimation()
    {
        _coAnimRoutine = StartCoroutine(CoPlayAnimation(2f));
    }

    public void StopAnimation()
    {
        if (_coAnimRoutine != null)
        {
            StopCoroutine(_coAnimRoutine);
            _coAnimRoutine = null;
        }
    }

    public IEnumerator CoPlayAnimation(float duration)
    {
        float frameTime = duration / _frames.Length;

        while (true)
        {
            for (int i = 0; i < _frames.Length; i++)
            {
                image.sprite = _frames[i];
                yield return new WaitForSeconds(frameTime);
            }
        }
    }
}
