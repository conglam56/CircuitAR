using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Instrument : MonoBehaviour
{   
    public InstruConnector connection1;

    public InstruConnector connection2;        

    public bool IsConnected()
    {
        if (connection1.IsConnected() && connection2.IsConnected())
        {
            return true;
        }
        else
        {
            return false;
        }
    } 
    
    // Are both wires connected to this instrument closed
    public bool AreConnectedWiresClosed()
    {
        if (connection1.AreConnectedWiresClosed() && connection2.AreConnectedWiresClosed())
            return true;

        return false;
    }

    public List<String> GetConnectedInstrumentNames()
    {
        List<String> connectedInstruments = new List<string>();
        AddConnectedInstrumentNames(ref connectedInstruments, connection1.GetConnectedInstrumentNames());
        AddConnectedInstrumentNames(ref connectedInstruments, connection2.GetConnectedInstrumentNames());
        connectedInstruments.RemoveAll(ContainName);
        return connectedInstruments;
    }

    public Wire GetOtherConnectedWire(Wire wire)
    {
        List<Wire> wires = new List<Wire>();

        List<Wire> wiresLst1 = connection1.GetConnectedWires();
        List<Wire> wiresLst2 = connection2.GetConnectedWires();

        if (wiresLst1 != null)
            wires.AddRange(wiresLst1);
        if (wiresLst2 != null)
            wires.AddRange(wiresLst2);

        wires.RemoveAll(Connectedwire => Connectedwire.name == wire.name);

        if (wires.Count == 0)
            return null;
        else
            return wires[0];
    }

    public List<Wire> GetOtherConnectedWires(Wire wire)
    {
        List<Wire> wires = new List<Wire>();

        List<Wire> wiresLst1 = connection1.GetConnectedWires();
        List<Wire> wiresLst2 = connection2.GetConnectedWires();

        if (wiresLst1 != null)
            wires.AddRange(wiresLst1);
        if (wiresLst2 != null)
            wires.AddRange(wiresLst2);

        wires.RemoveAll(Connectedwire => Connectedwire.name == wire.name);

        return wires;
    }

    public Wire GetConnectionWire()
    {
        List<Wire> wires = new List<Wire>();

        List<Wire> wiresLst1 = connection1.GetConnectedWires();
        List<Wire> wiresLst2 = connection2.GetConnectedWires();

        if (wiresLst1 != null)
            wires.AddRange(wiresLst1);
        if (wiresLst2 != null)
            wires.AddRange(wiresLst2);

        if (wires == null || wires.Count == 0)
           return null;
        else
            return wires[0];
    }

    private bool ContainName(String s)
    {
        return s.Equals(this.gameObject.name);
    }

    private void AddConnectedInstrumentNames(ref List<string> connectedInstruments, List<string> instrumentNames)
    {
        foreach (string name in instrumentNames)
        {
            if (!connectedInstruments.Contains(name))
            {
                connectedInstruments.Add(name);
            }
        }
    }
    
    public void DisconnectConnections()
    {
        connection1.DisconnectConnections();
        connection2.DisconnectConnections();
    }
}
