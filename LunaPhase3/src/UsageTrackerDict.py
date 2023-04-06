from chat.UsageTracker import UsageTracker


class UsageTrackerDict(UsageTracker):
    def __init__(self):
        self.completion_tokens = 0
        self.prompt_tokens = 0
        self.total_tokens = 0
        self.total_requests = 0

    def track_usage(self, completion_tokens: int, prompt_tokens: int, total_tokens: int):
        self.completion_tokens += completion_tokens
        self.prompt_tokens += prompt_tokens
        self.total_tokens += total_tokens
        self.total_requests += 1