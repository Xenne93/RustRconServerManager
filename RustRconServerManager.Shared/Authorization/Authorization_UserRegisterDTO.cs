namespace RustRconServerManager.Shared.Authorization;


    public class Authorization_UserRegisterDTO
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public int SystemProfileId { get; set; }

        // Optioneel: extra info
        public string? ConfirmPassword { get; set; }
    }
