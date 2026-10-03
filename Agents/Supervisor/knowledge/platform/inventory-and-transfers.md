---
title: Blood inventory, expiry and inter-hospital transfers
---

# Blood packets

LifeLink tracks each unit of blood as a **packet** of 440 ml with a unique tracking number (for example PKT-00001234), the hospital that created it, blood group, collected date, created date, expiry date and current hospital. Hospital staff add collected blood as packets with **Add packets** on the Blood Inventory page (the collected date is required and cannot be in the future; today is allowed). Recorded donations to a hospital's own request also create packets. The tracking number, created-by hospital and created date never change. Only the hospital that created a packet can edit its blood group or collected date, and only while it still holds the packet and it is available. Staff never type stock numbers in: each blood group's count is the number of available packets. Every event (created, edited, reserved, transferred out or in, issued, donated, expired) is recorded in the packet's history.

# Blood Inventory page

For each blood group the page shows available units, the minimum threshold, capacity, packets expiring soon and the next expiry date. You can:

- set the **minimum threshold** per blood group; the inventory agent alerts you when stock falls below it;
- issue blood by choosing the exact packets and giving a reason (issued packets stay in the packet list with status Issued);
- open a packet to see its full history.

**Packet shelf life** (21 to 35 days, matching your blood bags) and the **expiry alert window** (for example 5 days) are set in your hospital profile. Shelf life applies to newly collected packets.

# Monitoring alerts

Every 30 minutes LifeLink checks stock. If a group is below its threshold, you are told which hospitals have spare stock. If packets will expire within your alert window, you are told which hospitals or open requests need that group, so you can offer them before they are wasted.

# Inter-Hospital Transfers

Approved hospitals can:

- **Request** blood from another hospital, or
- **Offer** blood to another hospital by choosing the packets to send (they are held until the other hospital answers).

The other hospital accepts or rejects; no doctor or AI approval is needed. When accepting a request, the sending hospital chooses exactly the requested number of packets. Accepted packets move straight away with all their details (same tracking number); they leave the sender's inventory and become available at the receiving hospital. The transfer record lists the tracking numbers sent. A rejected or withdrawn offer returns the held packets. A rejection needs a reason. The hospital that created a transfer can delete it while it is still pending; it then stays in the history as Cancelled.

# Emergency Center

The Emergency Center is for hospital-to-hospital support: raising an emergency alerts other approved hospitals that hold the same blood group so they can offer a transfer. To ask donors for help, create a **Critical** blood request (there is a shortcut on the Emergency Center page); it goes through the normal hospital and doctor approval.

# Donate Blood (hospitals)

Hospitals can donate from their inventory to public blood requests of other hospitals on the **Donate Blood** page. Choose available packets of the requested blood group; they are held for the request. There is no AI screening: the doctor assigned to the request approves (the units count as fulfilled and the packets are donated, or join the requesting hospital's stock when it created the request) or rejects with a reason (the packets return). A hospital cannot donate to requests sent to itself. When a hospital creates a blood request it must choose one of its own doctors, who approves it.
