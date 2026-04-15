namespace OCR_BACKEND.Modals
{
    public class ManageLockRequest
    {
        public int DocumentId { get; set; }
        public string Action { get; set; } 
        public int UserId{ get; set; }
    }
}
