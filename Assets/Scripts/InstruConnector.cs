using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class InstruConnector : MonoBehaviour
{
    bool isConnected = false;
    Instrument parentInstrument = null;
    List<Wire> ConnectedWires = null;
    AudioSource connectionSound = null;
    Text errorText = null;

    public bool IsConnected()
    {
        return isConnected;

    }

    //Is any wire connected to this connector closed
    public bool AreConnectedWiresClosed()
    {        
        foreach (Wire wire in ConnectedWires)
        {
            if (wire.IsConnected())
            {
                return true;
            }
        }
        return false;
    }

    internal void DisconnectConnection(WireConnector wireConnector)
    {
        FixedJoint[] fjComponents = gameObject.GetComponents<FixedJoint>();
        foreach (FixedJoint comp in fjComponents)
        {
            Rigidbody rigitbody1 = comp.connectedBody;

            Rigidbody rigitbody2 = wireConnector.GetComponent<Rigidbody>();

            if (rigitbody1 == rigitbody2)
            {
                Destroy(comp);
                ConnectedWires.Remove(wireConnector.PartOfWire);
                return;
            }           
        }
    }

    private void Start()
    {
        parentInstrument = gameObject.transform.parent.GetComponent<Instrument>();
        ConnectedWires = new List<Wire>();
        errorText = GameObject.Find("DisconnectionIndicator").GetComponent<Text>();
        connectionSound = GameObject.Find("ConnectionSound").GetComponent<AudioSource>();        
    }    

    private void ApplyConnection(Collision collision)
    {
        Vector3 pos = gameObject.transform.position;
        collision.gameObject.transform.position = new Vector3(pos.x, pos.y + 0.001f, pos.z); //       collision.GetContact(0).point;
        FixedJoint joint = gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = collision.rigidbody;        
        collision.rigidbody.constraints = RigidbodyConstraints.FreezeRotation;

        connectionSound.Play();

        //Set connected body information
        isConnected = true;
        WireConnector wireConnector = collision.gameObject.GetComponent<WireConnector>();
        Wire ConnectedWire = wireConnector.PartOfWire;
        ConnectedWires.Add(ConnectedWire);
        //wireConnector.ApplyConnection(parentInstrument);
        wireConnector.ApplyInstrConnection(this);
        //errorText.text = "Connection between " + parentInstrument + " and " + ConnectedWire.name;
        errorText.text = "";
        //errorText.text += "\nconnector name: " + this.name;
        //errorText.text += "\nWire connector name: " + wireConnector.name;
        //errorText.text += "\nWire connected instruments: ";
        //List<string> instruNames = ConnectedWire.GetConnectedInstrumentNames();
        //foreach (var instru in instruNames)
        //{
        //    errorText.text += instru + ", ";
        //}
    }

    public string GetParentInstrumetName()
    {
        return parentInstrument.gameObject.name;
    }

    public void DisconnectConnections()
    {
        if (isConnected)
        {
           // errorText.text = "Disconnecting all connections";
            //Disconnect the connection
            FixedJoint[] fjComponents = gameObject.GetComponents<FixedJoint>();
            foreach (FixedJoint comp in fjComponents)
            {
                Rigidbody obj = comp.connectedBody;
                if (obj != null)
                {
                    obj.SendMessage("Disconnect");
                    obj.constraints = RigidbodyConstraints.FreezeAll;
                }
                //else
                //{
                //    errorText.text = "No rigidbody was found in the joint";
                //}

                Destroy(comp);
            }

            //Reset connected game object information
            isConnected = false;
            ConnectedWires = new List<Wire>();
        }        
    }    

    private void OnCollisionEnter(Collision collision)
    {        
        if (collision.gameObject.tag == "Player")
        {
            ApplyConnection(collision);
        }
    }

    //private void OnCollisionExit(Collision collision)
    //{
    //    Debug.Log("OnCollisionExit called");
    //    WireConnector wireConnector = collision.gameObject.GetComponent<WireConnector>();
    //    if (wireConnector != null && wireConnector.GetConnectedIstrConnector() != this.name)
    //    {
    //        FixedJoint[] fjComponents = gameObject.GetComponents<FixedJoint>();
    //        foreach (FixedJoint comp in fjComponents)
    //        {
    //            Rigidbody obj = comp.connectedBody;
    //            if (obj != null && obj.gameObject.name == wireConnector.name)
    //            {
    //                obj.constraints = RigidbodyConstraints.FreezeAll;
    //                Destroy(comp);                    
    //            }                
    //        }
    //        Wire ConnectedWire = wireConnector.PartOfWire;
    //        ConnectedWires.Remove(ConnectedWire);
    //    }
    //}

    public List<string> GetConnectedInstrumentNames()
    {
        List<string> connectedInstruments = new List<string>();
        try
        {            
            foreach (Wire wire in ConnectedWires)
            {
                List<string> connectedInstr = wire.GetConnectedInstrumentNames();
                //if any wire is not currently connected to this instrument Connector, then remove the wire from the array
                if (!connectedInstr.Contains(parentInstrument.gameObject.name))
                {
                    ConnectedWires.Remove(wire);
                }
                foreach (string instrumentname in connectedInstr)
                {
                    if (!connectedInstruments.Contains(instrumentname))
                    {
                        connectedInstruments.Add(instrumentname);
                    }
                }
            }
        }
        catch(InvalidOperationException e)
        {
            Debug.Log("InvalidOperationException occurred");
            return connectedInstruments;
        }
        return connectedInstruments;
    }

    public List<Wire> GetConnectedWires()
    {
        if (ConnectedWires.Count > 0)
            return ConnectedWires;
        else
            return null;
    }

    public bool ContainsWire(Wire wire)
    {
        if (ConnectedWires.Contains(wire))
        {
            return true;
        }
        else
        {
            return false;
        }
    }


    /*public List<string> GetConnectedInstrumentNames()
    {
        List<string> connectedInstruments = new List<string>();
        FixedJoint[] fjComponents = gameObject.GetComponents<FixedJoint>();
        foreach (FixedJoint comp in fjComponents)
        {
            Rigidbody obj = comp.connectedBody;
            if (obj != null)
            {
                WireConnector wireConn = obj.GetComponent<WireConnector>();
                Wire parentWire = wireConn.PartOfWire;

                List<string> connectedInstr = parentWire.GetConnectedInstrumentNames();

                foreach (string instrumentname in connectedInstr)
                {
                    if (!connectedInstruments.Contains(instrumentname))
                    {
                        connectedInstruments.Add(instrumentname);
                    }
                }
            }
            else
            {
                errorText.text = "No rigidbody was found in the joint";
            }            
        }
        
        return connectedInstruments;
    }*/

}
