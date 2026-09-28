using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Transfer;
using LifeLink.Entities;
using LifeLink.Services.Inventory;
using LifeLink.Services.Notification;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Transfer
{
    /// <summary>
    /// Direct hospital-to-hospital blood transfers between approved (PHRSC-registered) hospitals. No doctor or AI
    /// approval: the counterpart hospital accepts or rejects. The sender always chooses the packets: an "Offer"
    /// (created by the sender) holds the selected packets until the receiver answers; a "Request" (created by the
    /// receiver) is approved by the sender selecting exactly the requested number of packets. Accepting moves the
    /// packets in one transaction: every packet field stays the same, only the owner changes, every move is audited.
    /// Only the creator may delete a pending transfer.
    /// </summary>
    public class TransferRequestService : ITransferRequestService
    {
        public const string TransferConflictMessage = "This transfer or its packets were just changed by someone else. Please refresh and try again.";

        private readonly AppDbContext _context;

        public TransferRequestService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<TransferRequestResponseDto> CreateTransferRequestAsync(TransferRequestCreateDto dto, Guid actingHospitalId, Guid? performedByUserId = null)
        {
            var type = NormalizeType(dto.TransferType);

            if (actingHospitalId == dto.CounterpartHospitalId)
            {
                throw new InvalidOperationException("SenderHospitalId and ReceiverHospitalId cannot be the same.");
            }

            if (!BloodGroup.IsValid(dto.BloodGroup))
            {
                throw new InvalidOperationException($"Invalid blood group: '{dto.BloodGroup}'.");
            }

            if (type == TransferTypes.Request && dto.UnitsRequested <= 0)
            {
                throw new InvalidOperationException("UnitsRequested must be greater than zero.");
            }

            var creator = await RequireActiveHospitalAsync(actingHospitalId);
            var counterpart = await RequireActiveHospitalAsync(dto.CounterpartHospitalId);

            var senderId = type == TransferTypes.Offer ? creator.HospitalId : counterpart.HospitalId;
            var receiverId = type == TransferTypes.Offer ? counterpart.HospitalId : creator.HospitalId;

            // An offer sends specific packets the offering hospital holds right now; they are held for this offer
            List<BloodPacket>? offered = null;
            if (type == TransferTypes.Offer)
            {
                offered = await InventoryLedger.RequireSelectablePacketsAsync(_context, senderId, dto.PacketIds, dto.BloodGroup);
                dto.UnitsRequested = offered.Count;
            }

            var now = DateTime.UtcNow;
            var request = new HospitalTransferRequest
            {
                TransferRequestId = Guid.NewGuid(),
                SenderHospitalId = senderId,
                ReceiverHospitalId = receiverId,
                BloodGroup = dto.BloodGroup,
                UnitsRequested = dto.UnitsRequested,
                TransferType = type,
                Status = TransferRequestStatus.Pending.ToString(),
                Notes = dto.Notes?.Trim() ?? string.Empty,
                RequestedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.HospitalTransferRequests.Add(request);
            if (offered != null)
            {
                await InventoryLedger.ReservePacketsAsync(_context, offered, request.TransferRequestId,
                    $"Held for transfer offer to {counterpart.Name}", performedByUserId);
            }
            await _context.Notifications.AddAsync(NotificationFactory.ForHospital(counterpart.HospitalId,
                type == TransferTypes.Offer ? "TransferOffered" : "TransferRequested",
                type == TransferTypes.Offer ? $"Blood Offer: {dto.UnitsRequested} x {dto.BloodGroup}" : $"Blood Transfer Request: {dto.UnitsRequested} x {dto.BloodGroup}",
                type == TransferTypes.Offer
                    ? $"{creator.Name} offers {dto.UnitsRequested} unit(s) of {dto.BloodGroup} to your hospital. Review it under Inter-Hospital Transfers."
                    : $"{creator.Name} requests {dto.UnitsRequested} unit(s) of {dto.BloodGroup} from your hospital. Review it under Inter-Hospital Transfers."));
            await InventoryLedger.SavePacketChangesAsync(_context);

            return await MapToResponseDtoAsync(request.TransferRequestId);
        }

        public async Task<TransferRequestResponseDto?> GetTransferRequestAsync(Guid id)
        {
            var request = await _context.HospitalTransferRequests
                .Include(r => r.SenderHospital)
                .Include(r => r.ReceiverHospital)
                .FirstOrDefaultAsync(r => r.TransferRequestId == id);

            if (request == null) return null;
            var dto = MapToResponseDto(request);
            await AddPacketDetailsAsync(dto, request);
            return dto;
        }

        /// <summary>All transfers (Admin), or the incoming and outgoing transfers of one hospital.</summary>
        public async Task<IEnumerable<TransferRequestResponseDto>> GetAllTransferRequestsAsync(Guid? hospitalId = null)
        {
            var query = _context.HospitalTransferRequests
                .Include(r => r.SenderHospital)
                .Include(r => r.ReceiverHospital)
                .AsQueryable();
            if (hospitalId.HasValue)
            {
                query = query.Where(r => r.SenderHospitalId == hospitalId || r.ReceiverHospitalId == hospitalId);
            }

            var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
            return list.Select(MapToResponseDto);
        }

        public async Task<IEnumerable<TransferRequestResponseDto>> GetPendingTransferRequestsAsync(Guid? hospitalId = null)
        {
            var all = await GetAllTransferRequestsAsync(hospitalId);
            return all.Where(r => r.Status == TransferRequestStatus.Pending.ToString());
        }

        /// <summary>
        /// The counterpart hospital accepts and the packets move from sender to receiver right away. For a "Request",
        /// the accepting sender selects exactly UnitsRequested of its packets (packetIds); for an "Offer", the packets
        /// the sender selected and held when offering move.
        /// </summary>
        public async Task<TransferRequestResponseDto> ApproveTransferRequestAsync(Guid id, Guid actingHospitalId, Guid? performedByUserId = null, IReadOnlyCollection<Guid>? packetIds = null)
        {
            var request = await RequirePendingAsync(id);
            RequireCounterpart(request, actingHospitalId);

            var sender = await RequireActiveHospitalAsync(request.SenderHospitalId);
            var receiver = await RequireActiveHospitalAsync(request.ReceiverHospitalId);

            List<BloodPacket> packets;
            if (request.TransferType == TransferTypes.Offer)
            {
                packets = await InventoryLedger.GetHeldPacketsAsync(_context, request.TransferRequestId);
                if (packets.Count == 0 || packets.Count != request.UnitsRequested)
                {
                    throw new InvalidOperationException(
                        "This offer has no reserved packets (it was created before packet selection). Reject it and ask the sender to offer the specific packets again.");
                }
                var expired = packets.FirstOrDefault(p => p.ExpiryDate <= DateTime.UtcNow);
                if (expired != null)
                {
                    throw new InvalidOperationException($"Packet {expired.TrackingNumber} expired while the offer was pending. Reject this offer and ask for a new one.");
                }
            }
            else
            {
                packets = await InventoryLedger.RequireSelectablePacketsAsync(_context, sender.HospitalId, packetIds, request.BloodGroup, request.UnitsRequested);
            }

            await InventoryLedger.MovePacketsAsync(_context, packets, receiver.HospitalId, request.TransferRequestId,
                TransactionType.TransferOut, TransactionType.TransferIn, $"Transferred to {receiver.Name}", $"Received from {sender.Name}", performedByUserId);

            var now = DateTime.UtcNow;
            request.Status = TransferRequestStatus.Completed.ToString();
            request.ApprovedAt = now;
            request.UpdatedAt = now;

            await _context.Notifications.AddAsync(NotificationFactory.ForHospital(CreatorHospitalId(request), "TransferAccepted",
                $"Transfer Accepted: {request.UnitsRequested} x {request.BloodGroup}",
                $"{(request.TransferType == TransferTypes.Offer ? receiver.Name : sender.Name)} accepted the transfer. {request.UnitsRequested} packet(s) moved from {sender.Name} to {receiver.Name}."));

            // Transfer token: accept, reject and withdraw at the same moment cannot both succeed
            await InventoryLedger.SavePacketChangesAsync(_context, TransferConflictMessage);
            return await GetTransferRequestAsync(id) ?? throw new KeyNotFoundException();
        }

        public async Task<TransferRequestResponseDto> RejectTransferRequestAsync(Guid id, Guid actingHospitalId, string? reason, Guid? performedByUserId = null)
        {
            var message = reason?.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new InvalidOperationException("A rejection reason is required.");
            }
            if (message.Length > 500)
            {
                throw new InvalidOperationException("The rejection reason cannot exceed 500 characters.");
            }

            var request = await RequirePendingAsync(id);
            RequireCounterpart(request, actingHospitalId);

            var now = DateTime.UtcNow;
            request.Status = TransferRequestStatus.Rejected.ToString();
            request.RejectionReason = message;
            request.RejectedAt = now;
            request.UpdatedAt = now;

            // A rejected offer's held packets return to the sender's available stock
            await InventoryLedger.ReleaseHeldPacketsAsync(_context, request.TransferRequestId, "Transfer offer rejected; packet available again", performedByUserId);

            var rejecterName = await HospitalNameAsync(actingHospitalId);
            await _context.Notifications.AddAsync(NotificationFactory.ForHospital(CreatorHospitalId(request), "TransferRejected",
                $"Transfer Rejected: {request.UnitsRequested} x {request.BloodGroup}",
                $"{rejecterName} rejected the transfer. Reason: {message}"));

            // Transfer token: accept, reject and withdraw at the same moment cannot both succeed
            await InventoryLedger.SavePacketChangesAsync(_context, TransferConflictMessage);
            return await MapToResponseDtoAsync(request.TransferRequestId);
        }

        /// <summary>The creator deletes a pending transfer: it leaves the active lists and stays in history as Cancelled.</summary>
        public async Task<TransferRequestResponseDto> DeleteTransferRequestAsync(Guid id, Guid actingHospitalId, Guid? performedByUserId = null)
        {
            var request = await _context.HospitalTransferRequests.FindAsync(id)
                          ?? throw new KeyNotFoundException($"Transfer request with ID '{id}' was not found.");

            if (CreatorHospitalId(request) != actingHospitalId)
            {
                throw new UnauthorizedAccessException("Only the hospital that created this transfer can delete it.");
            }

            if (request.Status != TransferRequestStatus.Pending.ToString())
            {
                throw new InvalidOperationException($"Only pending transfers can be deleted. This one is {request.Status}.");
            }

            var now = DateTime.UtcNow;
            request.Status = TransferRequestStatus.Cancelled.ToString();
            request.UpdatedAt = now;

            // A withdrawn offer's held packets return to the sender's available stock
            await InventoryLedger.ReleaseHeldPacketsAsync(_context, request.TransferRequestId, "Transfer offer withdrawn; packet available again", performedByUserId);

            var creatorName = await HospitalNameAsync(actingHospitalId);
            await _context.Notifications.AddAsync(NotificationFactory.ForHospital(CounterpartHospitalId(request), "TransferCancelled",
                $"Transfer Withdrawn: {request.UnitsRequested} x {request.BloodGroup}",
                $"{creatorName} withdrew its transfer {(request.TransferType == TransferTypes.Offer ? "offer" : "request")}."));

            // Transfer token: accept, reject and withdraw at the same moment cannot both succeed
            await InventoryLedger.SavePacketChangesAsync(_context, TransferConflictMessage);
            return await MapToResponseDtoAsync(request.TransferRequestId);
        }

        public static Guid CreatorHospitalId(HospitalTransferRequest r) =>
            r.TransferType == TransferTypes.Offer ? r.SenderHospitalId : r.ReceiverHospitalId;

        public static Guid CounterpartHospitalId(HospitalTransferRequest r) =>
            r.TransferType == TransferTypes.Offer ? r.ReceiverHospitalId : r.SenderHospitalId;

        private static string NormalizeType(string? type)
        {
            if (string.Equals(type, TransferTypes.Offer, StringComparison.OrdinalIgnoreCase)) return TransferTypes.Offer;
            if (string.IsNullOrWhiteSpace(type) || string.Equals(type, TransferTypes.Request, StringComparison.OrdinalIgnoreCase)) return TransferTypes.Request;
            throw new InvalidOperationException("TransferType must be 'Request' or 'Offer'.");
        }

        private async Task<HospitalTransferRequest> RequirePendingAsync(Guid id)
        {
            var request = await _context.HospitalTransferRequests.FindAsync(id)
                          ?? throw new KeyNotFoundException($"Transfer request with ID '{id}' was not found.");
            if (request.Status != TransferRequestStatus.Pending.ToString())
            {
                throw new InvalidOperationException($"This transfer is already {request.Status}.");
            }
            return request;
        }

        private static void RequireCounterpart(HospitalTransferRequest request, Guid actingHospitalId)
        {
            if (CounterpartHospitalId(request) != actingHospitalId)
            {
                throw new UnauthorizedAccessException("Only the hospital this transfer was sent to can accept or reject it.");
            }
        }

        /// <summary>Only approved (PHRSC-registered) hospitals that are not suspended take part in transfers.</summary>
        private async Task<Hospital> RequireActiveHospitalAsync(Guid hospitalId)
        {
            // An empty ID would otherwise look like an unknown hospital; keep the explicit guard
            if (hospitalId == Guid.Empty)
                throw new InvalidOperationException("A valid hospital ID is required.");

            var hospital = await _context.Hospitals.FindAsync(hospitalId)
                           ?? throw new InvalidOperationException("The selected hospital was not found.");
            if (!hospital.IsVerified)
                throw new InvalidOperationException($"{hospital.Name} is not an approved hospital.");
            if (hospital.IsSuspended)
                throw new InvalidOperationException("Suspended hospitals cannot take part in transfers.");
            return hospital;
        }

        private async Task<string> HospitalNameAsync(Guid hospitalId) =>
            await _context.Hospitals.Where(h => h.HospitalId == hospitalId).Select(h => h.Name).FirstOrDefaultAsync() ?? "A hospital";

        /// <summary>Packets moved by a completed transfer, or held by a pending offer, with their tracking numbers.</summary>
        private async Task AddPacketDetailsAsync(TransferRequestResponseDto dto, HospitalTransferRequest request)
        {
            var transferId = request.TransferRequestId;
            List<Guid> packetIds;
            if (request.Status == TransferRequestStatus.Pending.ToString())
            {
                packetIds = await _context.BloodPackets
                    .Where(p => p.HeldForReferenceId == transferId && p.Status == BloodPacketStatus.Reserved)
                    .Select(p => p.PacketId)
                    .ToListAsync();
            }
            else
            {
                packetIds = await _context.InventoryTransactions
                    .Where(t => t.ReferenceId == transferId && t.TransactionType == TransactionType.TransferOut && t.PacketId != null)
                    .Select(t => t.PacketId!.Value)
                    .ToListAsync();
            }

            var numbers = await _context.BloodPackets
                .Where(p => packetIds.Contains(p.PacketId))
                .ToDictionaryAsync(p => p.PacketId, p => p.TrackingNumber);
            dto.PacketIds = packetIds;
            dto.PacketTrackingNumbers = packetIds.Select(id => numbers.GetValueOrDefault(id) ?? string.Empty).OrderBy(n => n).ToList();
        }

        private async Task<TransferRequestResponseDto> MapToResponseDtoAsync(Guid id)
        {
            var request = await _context.HospitalTransferRequests
                .Include(r => r.SenderHospital)
                .Include(r => r.ReceiverHospital)
                .FirstAsync(r => r.TransferRequestId == id);

            return MapToResponseDto(request);
        }

        private static TransferRequestResponseDto MapToResponseDto(HospitalTransferRequest r)
        {
            return new TransferRequestResponseDto
            {
                TransferRequestId = r.TransferRequestId,
                SenderHospitalId = r.SenderHospitalId,
                SenderHospitalName = r.SenderHospital != null ? r.SenderHospital.Name : string.Empty,
                ReceiverHospitalId = r.ReceiverHospitalId,
                ReceiverHospitalName = r.ReceiverHospital != null ? r.ReceiverHospital.Name : string.Empty,
                BloodGroup = r.BloodGroup,
                UnitsRequested = r.UnitsRequested,
                Status = r.Status,
                Notes = r.Notes,
                TransferType = r.TransferType,
                CreatedByHospitalId = CreatorHospitalId(r),
                RejectionReason = r.RejectionReason,
                RequestedAt = r.RequestedAt,
                ApprovedAt = r.ApprovedAt,
                RejectedAt = r.RejectedAt,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            };
        }
    }
}
