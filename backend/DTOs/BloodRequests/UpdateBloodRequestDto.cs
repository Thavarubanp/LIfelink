namespace LifeLink.DTOs.BloodRequests
{
    /// <summary>
    /// The only fields a patient may change on their own request while it is still Pending. Hospital, priority, reason
    /// and every other field stay as created.
    /// </summary>
    public class UpdateBloodRequestDto
    {
        public string BloodGroup { get; set; } = string.Empty;
        public int UnitsRequired { get; set; }
    }
}
