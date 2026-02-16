using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class YukiFlower : MonoBehaviour, IEffect
{
    [SerializeField] private Image _image;

    [SerializeField] private Sprite[] _frames;
    [SerializeField] private bool _autoHide = true;

    private Coroutine _coAnimRoutine;

    private void Awake()
    {
        Texture2D sheet = Resources.Load<Texture2D>("effects/textures/FX_BI_Yuki_01SE");
        if (sheet == null)
        {
            //Debug.LogError($"Not found in Resources");
            return;
        }

        _frames = Util.Slice(sheet, 6, 6);
        if (_frames == null || _frames.Length == 0)
        {
            //Debug.LogError("Sprite slicing failed");
        }
            

        _image.enabled = false;
    }

    public void Play()
    {
        gameObject.SetActive(true);

        // �ߺ� ��� ����
        if (_coAnimRoutine != null)
            StopCoroutine(_coAnimRoutine);

        _coAnimRoutine = StartCoroutine(CoPlayAnimation(1f));
    }

    private IEnumerator CoPlayAnimation(float duration)
    {
        _image.enabled = true;

        float frameTime = duration / _frames.Length;

        for (int i = 0; i < _frames.Length; i++)
        {
            _image.sprite = _frames[i];
            yield return new WaitForSeconds(frameTime);
        }

        if (_autoHide)
        {
            _image.enabled = false;
        }

        _coAnimRoutine = null;
    }

    public void Stop()
    {
        throw new System.NotImplementedException();
    }
}
