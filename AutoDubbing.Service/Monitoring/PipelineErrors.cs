namespace AutoDubbing.Service.Monitoring;

public static class PipelineErrors
{
    public static string? Stage(string? value) => value is "translation" or "tts" or "upload" or "persistence" or "cache" ? value : null;
    public static (string Code, string Message) Explain(string? code) => code is not null && ErrorMessages.TryGetValue(code, out var text) ? (code, text) : ("UnknownFailure", "Chưa có mã lỗi đã phân loại; chưa xác định được nguyên nhân.");
    private static readonly IReadOnlyDictionary<string, string> ErrorMessages = new Dictionary<string, string>
    {
        ["SupabaseObjectMismatch"] = "File audio trên Supabase không khớp bytes hoặc kiểu WAV mong đợi.",
        ["SupabaseUnauthorized"] = "Supabase từ chối quyền truy cập; cần kiểm tra quyền upload.",
        ["SupabaseNotConfigured"] = "Cấu hình lưu trữ Supabase thiếu hoặc không hợp lệ.",
        ["SupabaseBucketNotFound"] = "Không tìm thấy bucket Supabase đã cấu hình.",
        ["SupabaseBucketNotPublic"] = "Bucket Supabase chưa cho phép đọc audio công khai.",
        ["SupabaseUnavailable"] = "Không kết nối được Supabase.",
        ["SupabaseServiceUnavailable"] = "Dịch vụ Supabase tạm thời không khả dụng.",
        ["SupabaseTimeout"] = "Hết thời gian chờ Supabase.",
        ["SupabaseUploadFailed"] = "Supabase không hoàn tất yêu cầu upload.",
        ["SupabaseQuotaExceeded"] = "Supabase đang giới hạn yêu cầu hoặc dung lượng.",
        ["SupabaseFileLimitExceeded"] = "Audio vượt giới hạn kích thước của Supabase.",
        ["SupabaseMimeRejected"] = "Supabase từ chối kiểu file audio.",
        ["SupabaseInvalidResponse"] = "Phản hồi Supabase không đúng định dạng mong đợi.",
        ["DailyQuotaExceeded"] = "Đã chạm quota ngày của dịch vụ tạo nội dung. Hết thời gian chờ không bảo đảm quota đã phục hồi.",
        ["QuotaExceeded"] = "Dịch vụ tạo nội dung đang giới hạn yêu cầu.",
        ["QuotaUnavailable"] = "Dịch vụ chưa cấp quota phù hợp; cần người vận hành kiểm tra.",
        ["GeminiUnauthorized"] = "Gemini từ chối xác thực.",
        ["GeminiForbidden"] = "Gemini từ chối quyền dùng dịch vụ hoặc model.",
        ["GeminiModelUnavailable"] = "Model Gemini đang cấu hình không khả dụng.",
        ["GeminiUnavailable"] = "Không kết nối được Gemini.",
        ["GeminiTimeout"] = "Hết thời gian chờ Gemini.",
        ["GeminiNotConfigured"] = "Cấu hình Gemini thiếu hoặc không hợp lệ.",
        ["GenerationNotConfigured"] = "Cấu hình dịch vụ tạo nội dung chưa hợp lệ.",
        ["GeminiRejected"] = "Gemini từ chối yêu cầu tạo nội dung.",
        ["GeminiInvalidResponse"] = "Gemini trả phản hồi không đúng định dạng mong đợi.",
        ["GenerationBlockedOrIncomplete"] = "Dịch vụ chặn nội dung hoặc chưa trả kết quả hoàn chỉnh.",
        ["InvalidTranslation"] = "Dịch vụ không trả bản dịch hợp lệ.",
        ["TranslationRequiresReview"] = "Bản dịch cần kiểm tra lại tên riêng hoặc thông tin trong nội dung gốc.",
        ["InvalidAudioResponse"] = "Dịch vụ không trả dữ liệu audio hợp lệ.",
        ["InvalidWav"] = "Dữ liệu audio không phải WAV hợp lệ.",
        ["InvalidAudioMetadata"] = "Thông tin định dạng audio không hợp lệ.",
        ["UnsupportedAudioFormat"] = "Định dạng audio chưa được hỗ trợ.",
        ["AudioTooLarge"] = "File audio vượt kích thước cho phép.",
        ["InvalidAudioUpload"] = "Đường dẫn hoặc checksum file upload không hợp lệ.",
        ["InvalidUploadedAudioUrl"] = "URL audio không khớp POI, ngôn ngữ hoặc nội dung.",
        ["PersistenceUnavailable"] = "Không hoàn tất việc lưu dữ liệu; xem bước thất bại để biết vị trí.",
        ["SourceChanged"] = "Nội dung gốc đã đổi trong lúc xử lý.",
        ["SourceTooLong"] = "Nội dung gốc vượt giới hạn độ dài.",
        ["UnsupportedSourceContent"] = "Nội dung hoặc ngôn ngữ gốc chưa được hỗ trợ.",
        ["PoiUnavailable"] = "POI không còn khả dụng để xử lý.",
        ["JobTimeout"] = "Tác vụ vượt thời gian xử lý cho phép.",
        ["WorkerStopped"] = "API dừng trong lúc tác vụ đang xử lý.",
        ["PreparationUnavailable"] = "Pipeline gặp lỗi xử lý chưa được phân loại.",
        ["GenerationFailed"] = "Tác vụ chưa hoàn tất và chưa có nguyên nhân chi tiết."
    };

}
