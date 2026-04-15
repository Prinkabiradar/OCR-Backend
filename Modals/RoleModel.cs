namespace OCR_BACKEND.Modals
{
    public class RoleModel
    {
        public int RoleId { get; set; }
        public string RoleCode { get; set; }
        public string RoleName { get; set; }
        public string RoleDescription { get; set; }
        public int UserId { get; set; }
        public bool IsActive { get; set; }
    }
}
