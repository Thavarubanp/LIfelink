---
title: LifeLink blood requests
---

# Creating a blood request

Donors, patients, hospital staff and the Admin can create a blood request from **Create Blood Request**. Choose the hospital (hospital staff always request for their own hospital), the blood group, the number of units (1 to 10), the priority (Normal, High or Critical) and the reason. Blood requests do not automatically expire based on elapsed time: priority controls urgency and notification behavior, not request lifetime, and requests close through explicit lifecycle actions.

# How a request is approved

1. The request is **Pending** until the hospital reviews it.
2. The hospital **verifies** it and assigns one of its doctors (status **Verified**), or rejects it with a reason.
3. The assigned doctor **approves** it (status **Approved**) or rejects it with a reason.
4. Approved requests appear in **Donate Blood / Available Requests**. Normal requests send no proactive alerts. High (Urgent) requests notify eligible donors whose saved blood group exactly matches the request, but do not alert hospitals. Critical requests notify those exact-group eligible donors and qualifying other hospitals whose valid exact-group stock is strictly above their own minimum threshold.

# Donated, reserved and remaining units

- **Donated (fulfilled) units** count only donations that were actually recorded by the hospital.
- **Reserved units** are donors a doctor has approved who have not donated yet. Each approval reserves one slot.
- **Remaining units** = units required minus donated units.

A request stays visible until the donated units reach the units required. When every remaining slot is reserved, the request still shows but new acceptances are paused ("All slots reserved"); if an approved donor withdraws or is released, the slot opens again. The request is **Completed** only when all required units have been donated.

# Deleting a request

Only the person who created a request can delete it, and completed requests cannot be deleted. Deleting removes it from all active lists but keeps its history: acceptances, screening reports, doctor decisions and recorded donations stay visible to everyone involved. Donors still in progress are released and notified.

# Request lifetime and packet expiry

Blood requests have no elapsed-time expiry. They remain governed by explicit approval, rejection, cancellation, deletion and completion actions. Blood-packet expiry is a separate, active inventory process and does not close a blood request.

# Critical requests

Normal requests send no proactive alerts. High (Urgent) requests alert eligible donors whose saved blood group exactly matches the request and do not alert hospitals. Critical requests alert those exact-group eligible donors plus qualifying other verified, non-suspended/non-blocked hospitals whose available, unexpired exact-group stock is strictly greater than their own minimum threshold; the requesting hospital is excluded. Users who have not saved their blood group are not alerted. These proactive alerts use exact-group targeting rather than the broader compatibility rules used when someone manually considers donation, and backend authorization determines the final recipients.
