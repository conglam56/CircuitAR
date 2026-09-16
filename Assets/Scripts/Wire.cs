using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Wire : MonoBehaviour
{
    [SerializeField]
    WireConnector connector1;

    [SerializeField]
    WireConnector connector2;    

    public bool IsConnected()
    {
        if (connector1.IsConnected() && connector2.IsConnected())
        {
            return true;
        }
        return false;
    }

    public List<string> GetConnectedInstrumentNames()
    {
        List<string> connectedInstruments = new List<string>();
        AddInstrumentNames(ref connectedInstruments, connector1.GetConnectedInstrumentName());
        AddInstrumentNames(ref connectedInstruments, connector2.GetConnectedInstrumentName());        
        return connectedInstruments;
    }

    public List<string> GetConnectedInstrumentNames(string instrumentName)
    {
        List<string> connectedInstruments = GetConnectedInstrumentNames();

        if (connectedInstruments.Contains(instrumentName))
        {
            connectedInstruments.Remove(instrumentName);
            return connectedInstruments;
        }
        else
        {
            // Something is wrong therefore returning null
            return null;
        }
    }

    public List<string> GetConnectedInstrumentNames(List<string> instrumentNames)
    {
        List<string> connectedInstruments = GetConnectedInstrumentNames();

        foreach(string instrumentName in instrumentNames)
        {
            if (connectedInstruments.Contains(instrumentName))
            {
                connectedInstruments.Remove(instrumentName);                
            }            
        }

        return connectedInstruments;
    }

    void AddInstrumentNames(ref List<string> lst, string InstrumentName)
    {
        if (string.IsNullOrEmpty(InstrumentName))
            return;

        if (lst.Contains(InstrumentName))
            return;

        lst.Add(InstrumentName);
    }

    //public string OtherConnectedInstrumentName(string instrument1)
    //{
    //    string instru1 = connector1.ConnectedInstrumentName;

    //    string instru2 = connector2.ConnectedInstrumentName;

    //    if (string.Compare(instru1, instrument1) == 0)
    //    {
    //        return instru2;
    //    }
    //    else if (string.Compare(instru2, instrument1) == 0)
    //    {
    //        return instru1;
    //    }//if instrument1 name does not match with the connected instruments of this wire then it is not connected to this wire, so return empty
    //    return "";
    //}
}
