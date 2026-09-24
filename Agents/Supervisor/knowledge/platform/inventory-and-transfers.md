---
title: Blood inventory, expiry and inter-hospital transfers
---

# Blood packets

LifeLink tracks each unit of blood as a **packet** of 440 ml with its own ID (shown as PKT-XXXXXXXX), blood group, collection date, expiry date and current hospital. Packets are created only from recorded donations; transfers move existing packets. Staff cannot type stock numbers in by hand. Every event (collected, transferred out or in, issued, expired) is recorded in the packet's history.

# Blood Inventory page

For each blood group the page shows available units, the minimum threshold, capacity, packets expiring soon and the next expiry date. You can:

- set the **minimum threshold** per blood group; the inventory agent alerts you when stock falls below it;
- issue blood from stock by lowering the unit count with a reason (the earliest-expiring packets are used first);
- open a packet to see its full history.

**Packet shelf life** (21 to 35 days, matching your blood bags) and the **expiry alert window** (for example 5 days) are set in your hospital profile. Shelf life applies to newly collected packets.

# Monitoring alerts

Every 30 minutes LifeLink checks stock. If a group is below its threshold, you are told which hospitals have spare stock. If packets will expire within your alert window, you are told which hospitals or open requests need that group, so you can offer them before they are wasted.

# Inter-Hospital Transfers

Approved hospitals can:

- **Request** blood from another hospital, or
- **Offer** blood to another hospital (you must hold enough unexpired packets).

The other hospital accepts or rejects; no doctor or AI approval is needed. When accepted, the earliest-expiring packets move straight away: the packet IDs stay the same, ownership changes to the receiving hospital, and both hospitals' stock and histories update together. A rejection needs a reason. The hospital that created a transfer can delete it while it is still pending; it then stays in the history as Cancelled.

# Emergency Center

The Emergency Center is for hospital-to-hospital support: raising an emergency alerts other approved hospitals that hold compatible blood so they can offer a transfer. To ask donors for help, create a **Critical** blood request (there is a shortcut on the Emergency Center page); it goes through the normal hospital and doctor approval.
