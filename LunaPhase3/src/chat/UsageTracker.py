import abc


class UsageTracker(abc.ABC):
    @abc.abstractmethod
    def track_usage(self, completion_tokens: int, prompt_tokens: int, total_tokens: int):
        pass
