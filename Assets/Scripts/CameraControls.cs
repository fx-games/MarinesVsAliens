using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CameraControls : MonoBehaviour
{
    private Camera gameCamera;
    
    public float zoomSpeed = 0.1f;
    public float smoothSpeed = 2.0f;
    public float minOrtho = 1.0f;
    public float targetOrtho;
    public float maxOrtho = 20.0f;
    private float currentZoom = 4.0f;
    private void Awake()
    {
        gameCamera = Camera.main;
        if (gameCamera == null)
        {
            throw new Exception("Game needs camera.");
        }

        targetOrtho = gameCamera.orthographicSize;
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
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
        
        //gameCamera.orthographicSize = targetOrtho;
    }

    private void MoveCamera(Vector3Int direction)
    {
        float moveSpeed = 8.0f;
             
        Vector3 movementVector = new Vector3(direction.x, direction.y, 0);
        movementVector.Normalize();
        Vector3 nextPosition = gameCamera.transform.position  + movementVector;
        
        gameCamera.transform.position = Vector3.Lerp(gameCamera.transform.position, nextPosition, Time.deltaTime * moveSpeed);
    }
}
