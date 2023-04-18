import json
import random

import requests


class TenorGif:
    def __init__(self, api_key: str, client_key: str, limit: int):
        self._api_key = api_key
        self._client_key = client_key
        self._limit = limit

    def find_gif(self, query: str) -> str | None:
        response = requests.get(f"https://tenor.googleapis.com/v2/search?q={query}&key={self._api_key}&client_key={self._client_key}&limit={str(self._limit)}")

        gif = None
        if response.status_code == 200:
            top_gifs = json.loads(response.content)
            gif_obj = random.choice(top_gifs['results'])
            try:
                gif = gif_obj["media_formats"]["gif"]["url"]
            except KeyError:
                print("issue finding gif format")

        return gif

