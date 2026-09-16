using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WireConnector : MonoBehaviour
{
    bool isConnected;
    string WireName = "";
    //Instrument ConnectedInstrument = null;
    InstruConnector ConnectedInstrConnector = null;

    [SerializeField]
    public Wire PartOfWire = null;

    float x, y, z = 0;

    public string GetConnectedIstrConnector()
    {
        if (ConnectedInstrConnector == null)
            return "";
        else
            return ConnectedInstrConnector.name;
    }

    public bool IsConnected()
    {
        return isConnected;
    }

    private void Start()
    {
        WireName = PartOfWire.gameObject.name;
    }

    private void LateUpdate()
    {
        if (Mathf.Abs(transform.localPosition.x) > 5 || Mathf.Abs(transform.localPosition.z) > 5 || !(transform.localPosition.y > 0.2f || transform.localPosition.y <= 3f))
            ResetPosition();
    }

    public void ApplyInstrConnection(InstruConnector connectedInstrumentConnector)
    {
        //if (isConnected)
        //{
        //    Disconnect();
        //    ConnectedInstrConnector.DisconnectConnection(this);
        //}

        isConnected = true;
        ConnectedInstrConnector = connectedInstrumentConnector;
    }

    //public void ApplyConnection(Instrument connectedInstrument)
    //{
    //    isConnected = true;
    //    ConnectedInstrument = connectedInstrument;
    //}

    public void Disconnect()
    {
        isConnected = false;
        ConnectedInstrConnector = null;
    }

    public string GetConnectedInstrumentName()
    {
        if (ConnectedInstrConnector == null)
            return "";
        else
            return ConnectedInstrConnector.GetParentInstrumetName();
    }

    void ResetPosition()
    {
        gameObject.GetComponent<Rigidbody>().constraints = RigidbodyConstraints.FreezeAll;

        // set x
        if (gameObject.transform.localPosition.x > 5)
            x = 4.8f;
        else if (gameObject.transform.localPosition.x < -5)
            x = -4.8f;
        else
            x = gameObject.transform.localPosition.x;

        // set y
        if (gameObject.transform.localPosition.y > 3f || gameObject.transform.localPosition.y <= 0.2f)
            y = 0.3f;        
        else
            y = gameObject.transform.localPosition.y;

        // set z
        if (gameObject.transform.localPosition.z > 5)
            z = 4.8f;
        else if (gameObject.transform.localPosition.z < -5)
            z = -4.8f;
        else
            z = gameObject.transform.localPosition.z;

        transform.localPosition = new Vector3(x, y, z);        
    }
}
