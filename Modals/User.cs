namespace OCR_BACKEND.Modals
{
    public class User
    {
        public int UserId { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public int RoleId { get; set; }
        public string UserPass { get; set; }
        public bool IsActive { get; set; }
    }
}
