using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RetryMenu : MonoBehaviour
{
    public GameObject RetryCanvas;
    public GameObject PauseCanvas;
    public GameObject UICanvas;
    public Level level;

    public void ShowMenu()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        UICanvas.SetActive(false);
        PauseCanvas.SetActive(false);
        RetryCanvas.SetActive(true);
    }

    public void Retry()
    {
        RetryCanvas.SetActive(false);
        PauseCanvas.SetActive(true);
        UICanvas.SetActive(true);
        level.RetryLevel();
    }
}
