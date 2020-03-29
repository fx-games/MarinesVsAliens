using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CameraControls : MonoBehaviour
{
    private Camera gameCamera;
    private Vector3 touchStart;
    public float minZoom = 1.0f;
    public float maxZoom = 20.0f;
    private float touchStartTime;

    private void Awake()
    {
        gameCamera = Camera.main;
        if (gameCamera == null)
        {
            throw new Exception("Game needs camera.");
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        KeyboardScrolling();
        TouchScrollZoom();
        // mousewheel zoom
        Zoom(Input.GetAxis("Mouse ScrollWheel"));
    }

    private void TouchScrollZoom()
    {
        if (Input.GetMouseButtonDown(0))
        {
            touchStart = gameCamera.ScreenToWorldPoint(Input.mousePosition);
            touchStartTime = Time.time;
        }

        if (Input.touchCount == 2)
        {
            Touch touchZero = Input.GetTouch(0);
            Touch touchOne = Input.GetTouch(1);
            Vector2 touchZeroPrevPos = touchZero.position - touchZero.deltaPosition;
            Vector2 touchOnePrevPos = touchOne.position - touchOne.deltaPosition;

            float prevMagnitude = (touchZeroPrevPos - touchOnePrevPos).magnitude;
            float currentMagnitude = (touchZero.position - touchOne.position).magnitude;

            float difference = currentMagnitude - prevMagnitude;

            Zoom(difference * 0.01f);
            ActiveTouchCameraMovement = true;
        }
        else if (Input.GetMouseButton(0))
        {
            Vector3 direction = touchStart - gameCamera.ScreenToWorldPoint(Input.mousePosition);
            float touchTime = Time.time - touchStartTime;
            
            if (touchTime > 0.2f)
            {
                gameCamera.transform.position += direction;
                ActiveTouchCameraMovement = true;
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            ActiveTouchCameraMovement = false;
        }
    }

    public bool ActiveTouchCameraMovement { get; private set; }

    private void KeyboardScrolling()
    {
        Vector3Int movementVector = Vector3Int.zero;
        if (Input.GetKey(KeyCode.Q))
        {
            //gameCamera.fieldOfView += zoomSpeed;
        }
        else if (Input.GetKey(KeyCode.E))
        {
            //gameCamera.fieldOfView += zoomSpeed;
        }

        if (Input.GetKey(KeyCode.A))
        {
            movementVector += Vector3Int.left;
        }
        else if (Input.GetKey(KeyCode.D))
        {
            movementVector += Vector3Int.right;
        }

        if (Input.GetKey(KeyCode.W))
        {
            movementVector += Vector3Int.up;
        }
        else if (Input.GetKey(KeyCode.S))
        {
            movementVector += Vector3Int.down;
        }

        if (movementVector != Vector3Int.zero)
        {
            MoveCamera(movementVector);
        }
    }

    private void MoveCamera(Vector3Int direction)
    {
        float moveSpeed = 8.0f;
             
        Vector3 movementVector = new Vector3(direction.x, direction.y, 0);
        movementVector.Normalize();
        Vector3 nextPosition = gameCamera.transform.position  + movementVector;
        
        gameCamera.transform.position = Vector3.Lerp(gameCamera.transform.position, nextPosition, Time.deltaTime * moveSpeed);
    }
    
    private IEnumerator LerpFromTo(Vector3 pos1, Vector3 pos2, float duration) {
        for (float t=0f; t<duration; t += Time.deltaTime) {
            transform.position = Vector3.Lerp(pos1, pos2, t / duration);
            yield return 0;
        }
        transform.position = pos2;
    }

    public void SmoothTransition(Vector3 targetPos)
    {
        var realTargetPos = new Vector3(targetPos.x, targetPos.y, transform.position.z);
        StartCoroutine(LerpFromTo(transform.position, realTargetPos, 0.3f));
    }

    void Zoom(float increment)
    {
        gameCamera.orthographicSize = Mathf.Clamp(gameCamera.orthographicSize - increment, minZoom, maxZoom);
    }
}
