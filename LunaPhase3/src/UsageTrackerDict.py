from dataclasses import dataclass

from chat.UsageTracker import UsageTracker


@dataclass
class UsageDict:
    completion_tokens: int
    prompt_tokens: int
    total_tokens: int
    total_requests: int


class UsageTrackerDict(UsageTracker):
    def __init__(self):
        self.model_usage_dict: dict[str, UsageDict] = {}

    def track_usage(self, model: str, completion_tokens: int, prompt_tokens: int, total_tokens: int):
        if model not in self.model_usage_dict:
            self.model_usage_dict[model] = UsageDict(completion_tokens=0, prompt_tokens=0, total_tokens=0, total_requests=0)
        self.model_usage_dict[model].completion_tokens += completion_tokens
        self.model_usage_dict[model].prompt_tokens += prompt_tokens
        self.model_usage_dict[model].total_tokens += total_tokens
        self.model_usage_dict[model].total_requests += 1
