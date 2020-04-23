using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WorldEffects;

public class SimpleAudio : MonoBehaviour
{
    private AudioSource localAudioSource;
    public event VoidEvent OnCompleted;
    public AudioClip[] audioClips;
    // Start is called before the first frame update
    void Start()
    {
        localAudioSource = GetComponent<AudioSource>();
        localAudioSource.PlayOneShot(audioClips[Random.Range(0, audioClips.Length)]);
        StartCoroutine(WaitForSound());
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    
    public IEnumerator WaitForSound()
    {
        yield return new WaitUntil(() => localAudioSource.isPlaying == false);
        // or yield return new WaitWhile(() => audiosource.isPlaying == true);
        OnCompleted?.Invoke(this);
    }
}
